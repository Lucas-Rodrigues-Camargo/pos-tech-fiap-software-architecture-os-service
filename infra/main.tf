terraform {
  required_version = ">= 1.11"
  backend "s3" {}
  required_providers {
    aws = { source = "hashicorp/aws", version = "~> 6.0" }
  }
}
provider "aws" { region = var.region }
variable "region" { default = "us-east-1" }
variable "cluster_name" { type = string }
variable "namespace" { default = "oficina-phase4" }
data "aws_caller_identity" "current" {}
resource "aws_sqs_queue" "dlq" {
  name                      = "oficina-os-dlq"
  sqs_managed_sse_enabled   = true
  message_retention_seconds = 1209600
}
resource "aws_sqs_queue" "input" {
  name                       = "oficina-os"
  sqs_managed_sse_enabled    = true
  visibility_timeout_seconds = 120
  redrive_policy             = jsonencode({ deadLetterTargetArn = aws_sqs_queue.dlq.arn, maxReceiveCount = 5 })
}
resource "aws_ecr_repository" "app" {
  name                 = "oficina-os"
  image_tag_mutability = "IMMUTABLE"
  image_scanning_configuration { scan_on_push = true }
}
resource "aws_iam_role" "app" {
  name = "oficina-phase4-os"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow", Action = ["sts:AssumeRole", "sts:TagSession"]
      Principal = { Service = "pods.eks.amazonaws.com" }
    }]
  })
}
resource "aws_eks_pod_identity_association" "app" {
  cluster_name    = var.cluster_name
  namespace       = var.namespace
  service_account = "oficina-os"
  role_arn        = aws_iam_role.app.arn
}
resource "aws_iam_role_policy" "app" {
  role = aws_iam_role.app.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      { Effect = "Allow", Action = ["sqs:ReceiveMessage", "sqs:DeleteMessage", "sqs:GetQueueAttributes"], Resource = aws_sqs_queue.input.arn },
      { Effect = "Allow", Action = ["sqs:SendMessage"], Resource = [
        "arn:aws:sqs:${var.region}:${data.aws_caller_identity.current.account_id}:oficina-billing", "arn:aws:sqs:${var.region}:${data.aws_caller_identity.current.account_id}:oficina-execution",
        "arn:aws:sqs:${var.region}:${data.aws_caller_identity.current.account_id}:oficina-os",
      ] }

    ]
  })
}
variable "vpc_id" { type = string }
variable "private_subnet_ids" { type = list(string) }
variable "workload_security_group_ids" { type = set(string) }
resource "aws_db_subnet_group" "database" {
  name       = "oficina-os"
  subnet_ids = var.private_subnet_ids
}
resource "aws_security_group" "database" {
  name        = "oficina-os-database"
  vpc_id      = var.vpc_id
  description = "Private PostgreSQL for os only"
}
resource "aws_vpc_security_group_ingress_rule" "database" {
  for_each                     = var.workload_security_group_ids
  security_group_id            = aws_security_group.database.id
  referenced_security_group_id = each.value
  ip_protocol                  = "tcp"
  from_port                    = 5432
  to_port                      = 5432
}
resource "aws_db_instance" "database" {
  identifier                  = "oficina-os"
  engine                      = "postgres"
  engine_version              = "16"
  instance_class              = "db.t3.micro"
  allocated_storage           = 20
  storage_encrypted           = true
  db_name                     = "os"
  username                    = "oficina_admin"
  manage_master_user_password = true
  db_subnet_group_name        = aws_db_subnet_group.database.name
  vpc_security_group_ids      = [aws_security_group.database.id]
  publicly_accessible         = false
  backup_retention_period     = 7
  deletion_protection         = true
  skip_final_snapshot         = false
  final_snapshot_identifier   = "oficina-os-final"
}
output "database_host" { value = aws_db_instance.database.address }
output "database_secret_arn" { value = aws_db_instance.database.master_user_secret[0].secret_arn }

output "queue_url" { value = aws_sqs_queue.input.url }
output "ecr_url" { value = aws_ecr_repository.app.repository_url }
