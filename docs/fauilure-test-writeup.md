# Forced Failure Test: High Availability Validation

## Objective

Validate that the application's high-availability design actually behaves as
intended under failure conditions, rather than assuming it does based on
architecture alone. Two tests were planned: a compute-tier (ECS/AZ) failure
test, and a database-tier (RDS Multi-AZ) failover test.

---

## Test 1: ECS Task Failure (Simulated AZ Failure)

### Method

- The ECS service runs 2 Fargate tasks behind an Application Load Balancer,
  one task per Availability Zone (`us-east-1a`, `us-east-1b`).
- A continuous monitoring loop polled the ALB's `/health` endpoint every
  second, logging timestamped results, to detect any dropped or failed
  requests in real time.
- The running task in `us-east-1a` was manually stopped
  (`aws ecs stop-task`) to simulate the loss of compute in one AZ, while
  the monitoring loop continued running and the `us-east-1b` task was left
  untouched.
- ALB target health and ECS service events were monitored in parallel to
  build an exact recovery timeline.

### Timeline

| Time (UTC-4) | Event |
|---|---|
| 09:59:45.820 | Task deregistered from ALB target group |
| 09:59:45.827 | Connection draining begins on stopped task |
| 09:59:46.786 | Replacement task started |
| 10:00:15.093 | Replacement task registered as a healthy target |
| 10:00:24.409 | Service reached steady state |

**Total recovery time: ~38.6 seconds**, fully automated, no manual
intervention beyond the initial simulated failure.

### Result

Across the entire test window (09:59:00–10:00:25, spanning well before the
task was stopped through full recovery), **zero failed or dropped requests
were observed** in the monitoring loop. Every single request returned
`200 OK`, including throughout the ~29-second window between the old task's
removal and the new task passing health checks.

### Why this worked

- `desired_count = 2` in the ECS service, deliberately spread one task per
  AZ via private subnets in each AZ
- ALB target group health checks (30s interval, 2 consecutive successes
  required) detected the new task and added it to rotation automatically
- The ALB continued routing 100% of traffic to the healthy `us-east-1b`
  task for the entire recovery window, so end users experienced no
  interruption at all

### Limitations of this test

- This simulates task-level failure via `stop-task`, not a true AZ-level
  network partition or infrastructure outage. A real AZ failure could
  behave differently (e.g., if the NAT Gateway or other shared per-AZ
  resources were also affected).
- Load during the test was light (1 request/second, single client). Behavior
  under heavier concurrent production-like load was not tested.
- This test covered the compute tier only — see Test 2 for the database
  tier, and its outcome.

---

## Test 2: RDS Multi-AZ Failover (Attempted — Blocked by Platform Constraint)

### Intent

The plan was to enable RDS Multi-AZ, force a failover
(`aws rds reboot-db-instance --force-failover`), and observe how the
application's EF Core connection pool handled the brief primary/standby
switch — mirroring the ECS test, but for the database tier.

### What happened instead

Enabling Multi-AZ failed with:

```
InvalidParameterCombination: VPC Multi-AZ DB Instances are not available
for engine: sqlserver-ex
```

SQL Server **Express Edition** does not support Multi-AZ at all — only
Web, Standard, and Enterprise editions do. Switching to
`sqlserver-web` was attempted next, which failed with:

```
FreeTierRestrictionError: This engine type isn't available with free
plan accounts.
```

AWS Free Tier accounts are restricted to `sqlserver-ex` only; the editions
that support Multi-AZ are excluded from Free Tier entirely. This is a
platform-level constraint, not a configuration error, and it was not
resolvable without either upgrading the AWS account plan or switching
database engines entirely (e.g., to PostgreSQL, which is Multi-AZ-capable
and Free-Tier-eligible).

### Decision

Rather than sink disproportionate time into a database engine migration
purely to exercise this one test, the RDS instance was reverted to its
original single-AZ `sqlserver-ex` configuration. This is documented here
as an honest finding rather than worked around silently.

### What this would look like in production

- Use SQL Server **Web Edition or higher** (outside Free Tier) to unlock
  Multi-AZ support if staying on SQL Server, accepting the added licensing
  cost.
- Alternatively, use a Multi-AZ-compatible, Free-Tier-friendly engine such
  as **PostgreSQL** — a common real-world choice specifically because of
  SQL Server's licensing and edition constraints around HA features.
- Either path would then allow validating the same failover behavior
  demonstrated in Test 1: forcing a primary/standby switch and confirming
  the application's connection pool reconnects cleanly, ideally with
  retry/backoff logic on transient connection errors during the switch.

---

## Overall Takeaway

The compute tier's high-availability design was tested under real, if
simulated, failure conditions and held up with zero dropped requests and a
sub-40-second self-healing time. The database tier's HA design could not be
validated in the same way due to a platform licensing constraint discovered
during testing — itself a legitimate and realistic finding, since this exact
constraint is a known reason many production systems choose PostgreSQL or
upgrade SQL Server editions specifically to enable Multi-AZ. Both outcomes,
successful and blocked, are documented here rather than only reporting the
result that worked.