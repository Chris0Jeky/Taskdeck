terraform {
  required_version = ">= 1.14.0, < 2.0.0"

  backend "s3" {}

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "= 6.35.1"
    }
  }
}

provider "aws" {
  region = var.aws_region
}

module "taskdeck" {
  source = "../../modules/single_node"

  environment                   = "prod"
  name_prefix                   = var.name_prefix
  aws_region                    = var.aws_region
  availability_zone             = var.availability_zone
  vpc_cidr                      = var.vpc_cidr
  public_subnet_cidr            = var.public_subnet_cidr
  ami_id                        = var.ami_id
  instance_type                 = var.instance_type
  ssh_key_name                  = var.ssh_key_name
  allowed_ingress_cidrs         = var.allowed_ingress_cidrs
  allowed_ssh_cidrs             = var.allowed_ssh_cidrs
  proxy_port                    = var.proxy_port
  api_image                     = var.api_image
  web_image                     = var.web_image
  jwt_secret_ssm_parameter_name = var.jwt_secret_ssm_parameter_name
  jwt_secret_kms_key_arn        = var.jwt_secret_kms_key_arn
  jwt_issuer                    = var.jwt_issuer
  jwt_audience                  = var.jwt_audience
  jwt_expiration_minutes        = var.jwt_expiration_minutes
  root_volume_size_gb           = var.root_volume_size_gb
  data_volume_size_gb           = var.data_volume_size_gb
  protect_data_volume           = var.protect_data_volume
  backup_bucket_force_destroy   = var.backup_bucket_force_destroy
  extra_tags                    = var.extra_tags
}

output "application_url" {
  value = module.taskdeck.application_url
}

output "backup_bucket_name" {
  value = module.taskdeck.backup_bucket_name
}

output "public_ip" {
  value = module.taskdeck.public_ip
}

output "ssh_command" {
  value = module.taskdeck.ssh_command
}

output "database_path" {
  value = module.taskdeck.database_path
}

output "data_volume_id" {
  value = module.taskdeck.data_volume_id
}
