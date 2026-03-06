output "application_url" {
  description = "Base URL for the Taskdeck reverse proxy."
  value       = "http://${aws_instance.taskdeck_host.public_ip}:${var.proxy_port}"
}

output "public_ip" {
  description = "Public IP address of the Taskdeck host."
  value       = aws_instance.taskdeck_host.public_ip
}

output "backup_bucket_name" {
  description = "S3 bucket name reserved for Taskdeck backups and exported artifacts."
  value       = aws_s3_bucket.backups.bucket
}

output "database_path" {
  description = "Database path used by the compose deployment on the host."
  value       = "/var/lib/taskdeck/taskdeck.db"
}

output "ssh_command" {
  description = "SSH command template for the host when ssh_key_name is configured."
  value       = var.ssh_key_name == null ? null : "ssh ubuntu@${aws_instance.taskdeck_host.public_ip}"
}
