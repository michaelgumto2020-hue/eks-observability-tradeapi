variable "aws_region" {
  default = "us-east-1"
}

variable "project_name" {
  default = "eks-observability"
}

variable "vpc_cidr" {
  default = "10.1.0.0/16" # different range from project 1, avoids any confusion
}

variable "availability_zones" {
  default = ["us-east-1a", "us-east-1b"]
}

variable "public_subnet_cidrs" {
  default = ["10.1.1.0/24", "10.1.2.0/24"]
}

variable "private_subnet_cidrs" {
  default = ["10.1.11.0/24", "10.1.12.0/24"]
}

variable "cluster_name" {
  default = "eks-tradeapi-cluster"
}