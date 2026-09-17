resource "aws_db_subnet_group" "main" {
  name       = "${var.project_name}-db-subnet-group"
  subnet_ids = aws_subnet.private[*].id

  tags = {
    Name = "${var.project_name}-db-subnet-group"
  }
}

resource "random_password" "db_password" {
  length  = 24
  special = false
}

resource "aws_security_group" "db_sg" {
  name        = "${var.project_name}-db-sg"
  description = "Allow inbound DB traffic only from EKS nodes"
  vpc_id      = aws_vpc.main.id

  ingress {
    description     = "SQL Server traffic from EKS node security group"
    from_port       = 1433
    to_port         = 1433
    protocol        = "tcp"
    security_groups = [aws_eks_cluster.main.vpc_config[0].cluster_security_group_id]
  }

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = {
    Name = "${var.project_name}-db-sg"
  }
}

resource "aws_secretsmanager_secret" "db_connection_string" {
  name                    = "${var.project_name}-db-connection-string"
  recovery_window_in_days = 0
}

resource "aws_secretsmanager_secret_version" "db_connection_string" {
  secret_id     = aws_secretsmanager_secret.db_connection_string.id
  secret_string = "Server=${aws_db_instance.main.address},1433;Database=TradeApiDb;User Id=tradeapiadmin;Password=${random_password.db_password.result};TrustServerCertificate=True;"
}

resource "aws_db_instance" "main" {
  identifier     = "${var.project_name}-db"
  engine         = "sqlserver-ex"
  engine_version = "15.00"
  instance_class = "db.t3.micro"

  allocated_storage = 20
  storage_type      = "gp3"

  username = "tradeapiadmin"
  password = random_password.db_password.result

  db_subnet_group_name   = aws_db_subnet_group.main.name
  vpc_security_group_ids = [aws_security_group.db_sg.id]

  multi_az            = false
  publicly_accessible = false
  skip_final_snapshot  = true

  tags = {
    Name = "${var.project_name}-db"
  }
}

output "rds_endpoint" {
  value = aws_db_instance.main.address
}