variable "environment" {
  description = "Environment name (dev, staging, prod)."
  type        = string
}

variable "name_prefix" {
  description = "Prefix used for AWS resource names."
  type        = string
}

variable "aws_region" {
  description = "AWS region for all resources."
  type        = string
}

variable "availability_zone" {
  description = "Availability zone for the public subnet and instance."
  type        = string
}

variable "vpc_cidr" {
  description = "CIDR block for the Taskdeck VPC."
  type        = string
}

variable "public_subnet_cidr" {
  description = "CIDR block for the public subnet."
  type        = string
}

variable "ami_id" {
  description = "AMI ID for the Taskdeck host (the baseline assumes Ubuntu 24.04 LTS or equivalent Debian-based image)."
  type        = string
}

variable "instance_type" {
  description = "EC2 instance type for the single-node host."
  type        = string
}

variable "ssh_key_name" {
  description = "Optional EC2 key pair name for SSH access."
  type        = string
  default     = null
}

variable "allowed_ingress_cidrs" {
  description = "CIDR blocks allowed to reach SSH and the reverse-proxy listener."
  type        = list(string)
}

variable "proxy_port" {
  description = "Public port exposed by the reverse proxy."
  type        = number
}

variable "api_image" {
  description = "Container image reference for the Taskdeck API."
  type        = string
}

variable "web_image" {
  description = "Container image reference for the Taskdeck web UI."
  type        = string
}

variable "jwt_secret" {
  description = "JWT signing secret injected into the compose env file. Supply via untracked tfvars or TF_VAR_jwt_secret."
  type        = string
  sensitive   = true
}

variable "jwt_issuer" {
  description = "JWT issuer value for the containerized deployment."
  type        = string
}

variable "jwt_audience" {
  description = "JWT audience value for the containerized deployment."
  type        = string
}

variable "jwt_expiration_minutes" {
  description = "JWT expiration minutes for the containerized deployment."
  type        = number
}

variable "root_volume_size_gb" {
  description = "Root volume size for the single-node host."
  type        = number
}

variable "backup_bucket_force_destroy" {
  description = "Whether the environment backup bucket may be force-destroyed by Terraform."
  type        = bool
}

variable "extra_tags" {
  description = "Additional AWS tags applied to created resources."
  type        = map(string)
  default     = {}
}
