terraform {
  required_version = ">= 1.5.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }

  backend "s3" {
    bucket         = "mgumto-tf-state-eks-observability" # match your actual bucket name
    key            = "eks-observability-tradeapi/terraform.tfstate"
    region         = "us-east-1"
    dynamodb_table = "terraform-locks-eks"
    encrypt        = true
  }
}

provider "aws" {
  region = var.aws_region
}