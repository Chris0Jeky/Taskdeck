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
  description = "CIDR blocks allowed to reach the reverse-proxy listener. Keep this limited to trusted admin ranges or an upstream TLS terminator."
  type        = list(string)
}

variable "allowed_ssh_cidrs" {
  description = "Optional CIDR blocks allowed to reach SSH. Defaults to allowed_ingress_cidrs when unset so operators can narrow admin access separately."
  type        = list(string)
  default     = null
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

variable "jwt_secret_ssm_parameter_name" {
  description = "Name of the SecureString SSM parameter that stores the JWT signing secret for the host bootstrap."
  type        = string
}

variable "jwt_secret_kms_key_arn" {
  description = "Optional customer-managed KMS key ARN used to decrypt the JWT secret SecureString parameter."
  type        = string
  default     = null
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

variable "data_volume_size_gb" {
  description = "Persistent EBS data volume size for the Taskdeck SQLite database and related state."
  type        = number
}

variable "protect_data_volume" {
  description = "Whether Terraform should protect the persistent Taskdeck data volume from destroy operations."
  type        = bool
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
