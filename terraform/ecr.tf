resource "aws_ecr_repository" "app" {
  name                 = "${var.project_name}-app"
  image_tag_mutability = "IMMUTABLE" # lesson carried over from project #1's Trivy findings

  image_scanning_configuration {
    scan_on_push = true
  }

  force_delete = true # safe here — images rebuilt fresh via CI each session; not a pattern for real prod registries

  tags = {
    Name = "${var.project_name}-ecr"
  }
}

output "ecr_repository_url" {
  value = aws_ecr_repository.app.repository_url
}