data "aws_caller_identity" "current" {}
data "aws_partition" "current" {}

locals {
  base_name = "${var.name_prefix}-${var.environment}"
  common_tags = merge(
    {
      Application = "Taskdeck"
      Environment = var.environment
      ManagedBy   = "Terraform"
    },
    var.extra_tags,
  )
  backup_bucket_name            = lower(replace("${local.base_name}-${data.aws_caller_identity.current.account_id}-${var.aws_region}-backups", "_", "-"))
  jwt_secret_ssm_parameter_path = startswith(var.jwt_secret_ssm_parameter_name, "/") ? var.jwt_secret_ssm_parameter_name : "/${var.jwt_secret_ssm_parameter_name}"
  jwt_secret_ssm_parameter_arn  = "arn:${data.aws_partition.current.partition}:ssm:${var.aws_region}:${data.aws_caller_identity.current.account_id}:parameter${local.jwt_secret_ssm_parameter_path}"
}

resource "aws_vpc" "this" {
  cidr_block           = var.vpc_cidr
  enable_dns_support   = true
  enable_dns_hostnames = true

  tags = merge(local.common_tags, {
    Name = "${local.base_name}-vpc"
  })
}

resource "aws_internet_gateway" "this" {
  vpc_id = aws_vpc.this.id

  tags = merge(local.common_tags, {
    Name = "${local.base_name}-igw"
  })
}

resource "aws_subnet" "public" {
  vpc_id                  = aws_vpc.this.id
  cidr_block              = var.public_subnet_cidr
  availability_zone       = var.availability_zone
  map_public_ip_on_launch = true

  tags = merge(local.common_tags, {
    Name = "${local.base_name}-public-subnet"
    Tier = "public"
  })
}

resource "aws_route_table" "public" {
  vpc_id = aws_vpc.this.id

  route {
    cidr_block = "0.0.0.0/0"
    gateway_id = aws_internet_gateway.this.id
  }

  tags = merge(local.common_tags, {
    Name = "${local.base_name}-public-rt"
  })
}

resource "aws_route_table_association" "public" {
  subnet_id      = aws_subnet.public.id
  route_table_id = aws_route_table.public.id
}

resource "aws_security_group" "taskdeck_host" {
  name        = "${local.base_name}-sg"
  description = "Ingress for the Taskdeck single-node host"
  vpc_id      = aws_vpc.this.id

  ingress {
    description = "HTTP reverse proxy"
    from_port   = var.proxy_port
    to_port     = var.proxy_port
    protocol    = "tcp"
    cidr_blocks = var.allowed_ingress_cidrs
  }

  ingress {
    description = "SSH"
    from_port   = 22
    to_port     = 22
    protocol    = "tcp"
    cidr_blocks = var.allowed_ingress_cidrs
  }

  egress {
    description = "Allow all outbound traffic"
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = merge(local.common_tags, {
    Name = "${local.base_name}-sg"
  })
}

resource "aws_s3_bucket" "backups" {
  bucket        = local.backup_bucket_name
  force_destroy = var.backup_bucket_force_destroy

  tags = merge(local.common_tags, {
    Name    = "${local.base_name}-backups"
    Purpose = "taskdeck-backups"
  })
}

resource "aws_s3_bucket_versioning" "backups" {
  bucket = aws_s3_bucket.backups.id

  versioning_configuration {
    status = "Enabled"
  }
}

resource "aws_s3_bucket_server_side_encryption_configuration" "backups" {
  bucket = aws_s3_bucket.backups.id

  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

resource "aws_s3_bucket_public_access_block" "backups" {
  bucket                  = aws_s3_bucket.backups.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_iam_role" "taskdeck_host" {
  name = "${local.base_name}-ec2-role"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Action = "sts:AssumeRole"
        Effect = "Allow"
        Principal = {
          Service = "ec2.amazonaws.com"
        }
      }
    ]
  })

  tags = merge(local.common_tags, {
    Name = "${local.base_name}-ec2-role"
  })
}

resource "aws_iam_role_policy" "taskdeck_backups" {
  name = "${local.base_name}-backup-bucket-access"
  role = aws_iam_role.taskdeck_host.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Sid    = "TaskdeckBackupBucketAccess"
        Effect = "Allow"
        Action = [
          "s3:GetObject",
          "s3:PutObject",
          "s3:DeleteObject",
          "s3:ListBucket"
        ]
        Resource = [
          aws_s3_bucket.backups.arn,
          "${aws_s3_bucket.backups.arn}/*"
        ]
      }
    ]
  })
}

resource "aws_iam_role_policy" "taskdeck_jwt_secret" {
  name = "${local.base_name}-jwt-secret-access"
  role = aws_iam_role.taskdeck_host.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = concat(
      [
        {
          Sid    = "TaskdeckJwtSecretRead"
          Effect = "Allow"
          Action = [
            "ssm:GetParameter"
          ]
          Resource = [
            local.jwt_secret_ssm_parameter_arn
          ]
        }
      ],
      var.jwt_secret_kms_key_arn == null ? [] : [
        {
          Sid    = "TaskdeckJwtSecretDecrypt"
          Effect = "Allow"
          Action = [
            "kms:Decrypt"
          ]
          Resource = [
            var.jwt_secret_kms_key_arn
          ]
        }
      ],
    )
  })
}

resource "aws_iam_instance_profile" "taskdeck_host" {
  name = "${local.base_name}-ec2-profile"
  role = aws_iam_role.taskdeck_host.name
}

resource "aws_instance" "taskdeck_host" {
  ami                         = var.ami_id
  instance_type               = var.instance_type
  availability_zone           = var.availability_zone
  subnet_id                   = aws_subnet.public.id
  vpc_security_group_ids      = [aws_security_group.taskdeck_host.id]
  associate_public_ip_address = true
  iam_instance_profile        = aws_iam_instance_profile.taskdeck_host.name
  key_name                    = var.ssh_key_name
  user_data_replace_on_change = true

  metadata_options {
    http_endpoint = "enabled"
    http_tokens   = "required"
  }

  root_block_device {
    volume_size           = var.root_volume_size_gb
    volume_type           = "gp3"
    encrypted             = true
    delete_on_termination = true
  }

  user_data = templatefile("${path.module}/user_data.sh.tftpl", {
    api_image                     = var.api_image
    web_image                     = var.web_image
    jwt_secret_ssm_parameter_name = local.jwt_secret_ssm_parameter_path
    jwt_issuer                    = var.jwt_issuer
    jwt_audience                  = var.jwt_audience
    jwt_expiration_minutes        = var.jwt_expiration_minutes
    proxy_port                    = var.proxy_port
    backup_bucket_name            = aws_s3_bucket.backups.bucket
    aws_region                    = var.aws_region
  })

  tags = merge(local.common_tags, {
    Name = "${local.base_name}-host"
    Role = "taskdeck-single-node"
  })
}
