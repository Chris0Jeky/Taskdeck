variable "name_prefix" {
  type    = string
  default = "taskdeck"
}

variable "aws_region" {
  type = string
}

variable "availability_zone" {
  type = string
}

variable "vpc_cidr" {
  type = string
}

variable "public_subnet_cidr" {
  type = string
}

variable "ami_id" {
  type = string
}

variable "instance_type" {
  type = string
}

variable "ssh_key_name" {
  type    = string
  default = null
}

variable "allowed_ingress_cidrs" {
  type = list(string)
}

variable "proxy_port" {
  type    = number
  default = 80
}

variable "api_image" {
  type = string
}

variable "web_image" {
  type = string
}

variable "jwt_secret_ssm_parameter_name" {
  type = string
}

variable "jwt_secret_kms_key_arn" {
  type    = string
  default = null
}

variable "jwt_issuer" {
  type    = string
  default = "Taskdeck"
}

variable "jwt_audience" {
  type    = string
  default = "TaskdeckUsers"
}

variable "jwt_expiration_minutes" {
  type    = number
  default = 1440
}

variable "root_volume_size_gb" {
  type    = number
  default = 80
}

variable "backup_bucket_force_destroy" {
  type    = bool
  default = false
}

variable "extra_tags" {
  type    = map(string)
  default = {}
}
