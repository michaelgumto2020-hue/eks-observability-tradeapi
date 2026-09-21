# EKS Observability: TradeApi on Kubernetes

A Kubernetes (EKS) deployment of the same .NET trading API from
[`aws-ha-three-tier-app`](https://github.com/michaelgumto2020-hue/aws-ha-three-tier-app),
this time with a self-hosted Prometheus/Grafana observability stack, defined
SLOs, and a real induced-failure test that uncovered — and led to fixing — a
genuine gap in the alerting design.

**This is not a repeat of project #1.** The point of this project is to show
the same application running on a different orchestration platform, with a
proper observability layer built on top of it, and to demonstrate real SRE
practice: define SLOs, alert on them, break something on purpose, and prove
the alerting actually works — including finding out when it doesn't.

---

## Architecture

```
                              Internet
                                 │
                        ┌────────▼────────┐
                        │   LoadBalancer   │
                        │  (K8s Service)   │
                        └────────┬────────┘
                                 │
                  ┌──────────────┴──────────────┐
                  │                              │
           ┌──────▼──────┐                ┌──────▼──────┐
           │  TradeApi    │                │  TradeApi    │
           │  Pod (az-a)  │                │  Pod (az-b)  │
           │ EKS/c7i-flex │                │ EKS/c7i-flex │
           └──────┬──────┘                └──────┬──────┘
                  │                              │
                  └──────────────┬───────────────┘
                                 │
                  ┌──────────────┼──────────────┐
                  │                             │
           ┌──────▼──────┐              ┌───────▼───────┐
           │  RDS SQL     │              │   SQS Queue    │
           │  Server      │              │  + Dead Letter │
           │(private sub.)│              │     Queue      │
           └──────────────┘              └────────────────┘

  ┌─────────────────────────────────────────────────────────┐
  │                  Observability Stack                     │
  │  Prometheus  ──scrapes──▶  TradeApi /metrics              │
  │  Prometheus  ──evaluates──▶  PrometheusRule (SLO alerts)   │
  │  Alertmanager ◀──receives── Prometheus                     │
  │  Grafana  ──queries──▶  Prometheus (SLO dashboard)          │
  └─────────────────────────────────────────────────────────┘
```

**Networking:** dedicated VPC (separate CIDR range from project #1), public +
private subnets across 2 AZs, subnets tagged per EKS/ELB discovery
conventions (`kubernetes.io/role/elb`, `kubernetes.io/role/internal-elb`).

**Compute:** EKS managed cluster + managed node group (2 × `c7i-flex.large`
nodes), running TradeApi as a standard Kubernetes Deployment (2 replicas)
behind a `LoadBalancer`-type Service.

**Data:** RDS SQL Server Express in private subnets; SQS with a dead-letter
queue. Credentials wired in via a Kubernetes Secret (see
[Deliberate Tradeoffs](#deliberate-tradeoffs) for the honest limitations of
this approach compared to project #1's Secrets Manager + ECS task-role
integration).

**Observability:** self-hosted `kube-prometheus-stack` (Prometheus, Grafana,
Alertmanager, node-exporter, kube-state-metrics) via Helm. TradeApi is
instrumented with `prometheus-net.AspNetCore`, exposing `/metrics`, scraped
via a `ServiceMonitor`.

**CI/CD:** GitHub Actions builds and pushes to ECR (commit-SHA-tagged, since
the repo uses immutable ECR tags), then runs `kubectl set image` +
`kubectl rollout status` directly from the workflow — a fuller CD loop than
project #1's ECS version, since this one also drives the actual deployment
step, not just the image build.

---

## Repo Structure

```
eks-observability-tradeapi/
├── terraform/          # VPC, EKS cluster, node group, RDS, SQS, ECR, IAM
├── bootstrap/           # One-time S3 state bucket + DynamoDB lock table
├── app/                  # TradeApi (same app as project #1)
├── k8s/
│   ├── namespace.yaml
│   ├── deployment.yaml
│   ├── service.yaml
│   ├── secret.example.yaml   # secret.yaml itself is gitignored
│   ├── servicemonitor.yaml
│   └── prometheusrule.yaml
├── docs/
│   └── failure-test-writeup.md   # SLOs, induced outage, blind spot found & fixed
├── .github/workflows/
│   └── build-and-push.yml         # build → push → kubectl set image → rollout wait
└── README.md
```

---

## SLOs and the Incident That Tested Them

Two SLOs were defined for this project:

| SLI | SLO Target |
|---|---|
| Availability | 99.5% of requests non-5xx |
| p95 latency | < 300ms |

Both are visualized on a Grafana dashboard and backed by Prometheus alerting
rules. An induced full-outage test (`kubectl scale --replicas=0`) revealed
that the original availability alert — built on a request-ratio metric —
**could not detect a total outage**, since zero running pods means zero
metrics, not a bad ratio. A second alert (`TradeApiNoTargets`, using
`absent()`) was added specifically to close this gap, and was validated live
with full timestamped evidence across Prometheus, Alertmanager, and an
external health-check loop.

Full details, the complete timeline, and several other real issues found and
fixed along the way (Terraform state drift, a stale-secret bug, a
ServiceMonitor discovery failure, and a database migration race condition):
[`docs/failure-test-writeup.md`](docs/failure-test-writeup.md)

---

## ECS vs. EKS — Same App, Two Platforms

Having built the same application on both ECS Fargate (project #1) and EKS
(this project), a few concrete differences worth noting:

| | ECS (project #1) | EKS (this project) |
|---|---|---|
| Deployment unit | Task definition + Service (AWS-native) | Deployment + Service (Kubernetes-native) |
| Redeploy mechanism | `aws ecs update-service --force-new-deployment` | `kubectl set image` + `kubectl rollout status` |
| Health checks | ALB target group health checks | Kubernetes readiness/liveness probes |
| Secrets | AWS Secrets Manager, injected via `secrets` block, IAM-gated | Kubernetes Secret (base64 only, not encrypted) — a real security step down, discussed below |
| Observability | CloudWatch Logs + manual `describe-*` CLI calls | Self-hosted Prometheus/Grafana, real dashboards and alerting |
| Operational overhead | Lower — AWS manages scheduling entirely | Higher — cluster, node group, Helm releases, CRDs all need managing |
| Cost while running | Fargate billed per task | EC2 nodes billed continuously regardless of load |

**When I'd choose ECS:** a straightforward service that doesn't need
Kubernetes-specific tooling, where minimizing operational surface area
matters more than platform flexibility.

**When I'd choose EKS:** when the team already has Kubernetes expertise,
needs portability across clouds, or needs the ecosystem of Kubernetes-native
tooling (like the observability stack built here) that doesn't have an
equally mature AWS-native equivalent.

---

## Deliberate Tradeoffs

| Decision | Reasoning |
|---|---|
| Kubernetes Secret instead of Secrets Manager + IRSA | Faster to stand up for this project's scope; a real production setup would use IRSA-scoped access or the External Secrets Operator to pull live from Secrets Manager, avoiding the stale-secret class of bug documented in the failure-test writeup |
| SQS permissions on the node IAM role, not per-pod (no IRSA) | Every pod on the node inherits the permission, not just this one — less granular than project #1's ECS task role. IRSA would be the correct production fix |
| `c7i-flex.large` node instances | Chosen after `t3.micro`/`t3.medium` were rejected by Free Tier/AMI constraints; a real newer-generation Free Tier offer, verified directly against AWS's console rather than assumed |
| Self-hosted Prometheus/Grafana over Amazon Managed Prometheus/Grafana | More representative of the raw Helm/YAML skill set; AMP/AMG would reduce operational overhead in a real production setting |
| Fully destroyed and rebuilt between sessions | Same cost-control habit as project #1 — meant re-facing several of the "fresh infrastructure" bugs documented here (stale secrets, empty ECR, CRDs not yet installed) more than once, which is itself informative about the tradeoffs of fully ephemeral environments |

---

## Running This Yourself

```bash
# 1. One-time backend bootstrap
cd bootstrap
terraform init && terraform apply

# 2. Main infrastructure
cd ../terraform
terraform init
terraform apply

# 3. Connect kubectl
aws eks update-kubeconfig --region us-east-1 --name eks-tradeapi-cluster

# 4. Deploy the app
kubectl apply -f k8s/namespace.yaml
# fill in k8s/secret.yaml from k8s/secret.example.yaml using live Terraform outputs
kubectl apply -f k8s/secret.yaml
kubectl apply -f k8s/deployment.yaml
kubectl apply -f k8s/service.yaml

# 5. Build and push the image (triggers via GitHub Actions, or manually dispatch)

# 6. Install observability stack
kubectl create namespace monitoring
helm install monitoring prometheus-community/kube-prometheus-stack --namespace monitoring
kubectl apply -f k8s/servicemonitor.yaml
kubectl apply -f k8s/prometheusrule.yaml

# 7. Confirm
curl http://$(kubectl get service tradeapi -n tradeapi -o jsonpath='{.status.loadBalancer.ingress[0].hostname}')/health

# 8. Tear down when done — delete the Service first to avoid an orphaned load balancer
kubectl delete svc tradeapi -n tradeapi
cd terraform && terraform destroy
```

---

## What I'd Do Differently at Larger Scale

- IRSA (IAM Roles for Service Accounts) instead of node-level IAM permissions
- External Secrets Operator pulling live from Secrets Manager, eliminating
  the stale-secret failure mode entirely
- Amazon Managed Prometheus/Grafana to reduce the operational surface of
  self-hosting the observability stack
- A blackbox/external synthetic check (e.g., a separate uptime probe)
  alongside the metrics-based alerts, since this project's own failure test
  proved that internal application metrics alone cannot detect every class
  of outage
- Persistent (not fully ephemeral) infrastructure for anything beyond a
  learning project, to avoid repeatedly re-triggering "fresh environment"
  bugs like empty ECR repositories and uninstalled CRDs