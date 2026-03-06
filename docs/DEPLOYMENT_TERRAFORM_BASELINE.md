# Terraform Deployment Baseline

Last Updated: 2026-03-06  
Issue: `#102` OPS-10 Infrastructure-as-Code baseline for Taskdeck environments

This runbook defines the first Terraform baseline for Taskdeck.
It intentionally matches the current shipped deployment posture instead of inventing a second runtime:

- one public VPC + subnet per environment
- one single-node EC2 host running the existing Docker-based Taskdeck stack
- one encrypted S3 bucket for backup/export artifacts
- one persistent SQLite path on the host (`/var/lib/taskdeck/taskdeck.db`)

This baseline is deliberately single-node because the current product/runtime assumptions are still single-node biased:
- SQLite is the production data path today
- in-process workers run inside the API host
- SignalR/rate-limiting posture is not yet scale-out hardened

Related follow-through remains separate:
- `#101` staged rollout / canary / blue-green policy
- `#103` SBOM and provenance posture
- `#110` secrets/config management baseline
- `#111` cloud topology + autoscaling ADR
- `#84` managed production DB migration strategy

## Files

- `deploy/terraform/aws/modules/single_node/*`
- `deploy/terraform/aws/environments/dev/*`
- `deploy/terraform/aws/environments/staging/*`
- `deploy/terraform/aws/environments/prod/*`
- `scripts/deploy/Test-TaskdeckTerraformBaseline.ps1`
- `scripts/deploy/Invoke-TaskdeckTerraformDriftCheck.ps1`

## What Gets Provisioned

Each environment root provisions:

- networking
  - one VPC
  - one public subnet
  - one internet gateway
  - one public route table
  - one security group
- app hosting
  - one EC2 instance
  - one IAM role + instance profile
  - cloud-init bootstrap that installs Docker, writes the Taskdeck compose/env files, and starts the stack
- storage
  - one encrypted S3 bucket for exported artifacts/backups
- database resource
  - the Taskdeck SQLite file hosted on the EC2 root volume at `/var/lib/taskdeck/taskdeck.db`

## Secret and Config Handoff

Secrets are intentionally kept out of source control.

Use one of these handoff paths:

- untracked `terraform.tfvars`
- CI-injected `TF_VAR_*` environment variables
- a local `backend.hcl` file that is not committed

JWT signing secrets are no longer passed directly through `user_data`.
Instead, the environment root points the host bootstrap at an existing SecureString SSM parameter via `jwt_secret_ssm_parameter_name`.
The EC2 instance role receives `ssm:GetParameter` for that exact parameter, plus optional `kms:Decrypt` when `jwt_secret_kms_key_arn` is supplied for a customer-managed key.

Committed example files:

- `terraform.tfvars.example`
- `backend.hcl.example`

Do not commit:

- real JWT secret values
- real `jwt_secret_ssm_parameter_name` targets that would disclose production secret naming conventions
- cloud credentials
- remote-state backend credentials

This baseline stops at secret handoff mechanics. Rotation policy, provider credentials, and long-term secret storage posture stay in `#110`.

## Environment Differences

Environment differences are reviewable through the per-environment example files:

- `dev`
  - smaller instance, `8080` public port, force-destroy backup bucket enabled for disposable use
- `staging`
  - medium instance, `80` public port, backup bucket retention preserved
- `prod`
  - larger instance, `80` public port, tighter ingress and no force-destroy bucket

## Local Static Validation

Run the repo-level Terraform validation helper:

```powershell
powershell -File ./scripts/deploy/Test-TaskdeckTerraformBaseline.ps1
```

Equivalent raw commands per environment:

```powershell
terraform -chdir=deploy/terraform/aws/environments/dev init -backend=false -input=false
terraform -chdir=deploy/terraform/aws/environments/dev validate
```

Formatting check from repo root:

```powershell
terraform fmt -check -recursive deploy/terraform/aws
```

## Bootstrap Workflow

1. Copy the environment example files:

```powershell
Copy-Item deploy/terraform/aws/environments/staging/terraform.tfvars.example deploy/terraform/aws/environments/staging/terraform.tfvars
Copy-Item deploy/terraform/aws/environments/staging/backend.hcl.example deploy/terraform/aws/environments/staging/backend.hcl
```

2. Replace placeholders in the untracked files:
- `ami_id`
- `api_image`
- `web_image`
- `jwt_secret_ssm_parameter_name`
- optional `jwt_secret_kms_key_arn` when the SecureString uses a customer-managed CMK
- ingress CIDRs
- backend bucket/key values if using remote state

3. Ensure the SecureString parameter already exists before bootstrap:

```powershell
aws ssm put-parameter `
  --name /taskdeck/staging/jwt-secret `
  --type SecureString `
  --value "<strong-random-jwt-secret>" `
  --overwrite `
  --region eu-west-2
```

4. Initialize Terraform:

```powershell
terraform -chdir=deploy/terraform/aws/environments/staging init -input=false -backend-config=backend.hcl
```

5. Review the plan:

```powershell
terraform -chdir=deploy/terraform/aws/environments/staging plan -var-file=terraform.tfvars
```

6. Apply:

```powershell
terraform -chdir=deploy/terraform/aws/environments/staging apply -var-file=terraform.tfvars
```

The EC2 instance is intentionally configured with `user_data_replace_on_change = true`.
Changing bootstrap inputs such as container images, proxy port, or SSM parameter wiring replaces the host instead of silently leaving the old runtime in place.

## Post-Apply Checks

Use the Terraform outputs to verify the host is actually serving Taskdeck:

```powershell
terraform -chdir=deploy/terraform/aws/environments/staging output application_url
```

Then validate from a trusted network path:

- `GET {application_url}/health/ready` returns `200`
- `GET {application_url}/api/boards` returns `401`
- SSH to the host and confirm `docker compose ps` under `/opt/taskdeck`

Example:

```powershell
curl http://<public-ip>/health/ready
curl -i http://<public-ip>/api/boards
```

## TLS Boundary

This module intentionally leaves Nginx on plain HTTP inside the instance so it can sit behind a separate HTTPS edge.
That means:

- direct internet exposure of the host listener is not an acceptable production posture for credentialed traffic
- `allowed_ingress_cidrs` should be limited to trusted admin ranges or the private/source ranges of an upstream TLS terminator
- if the environment must be internet-facing, put an ALB, CDN, reverse tunnel, or equivalent HTTPS endpoint in front and lock the security group down to that edge

## Drift Detection

Use the dedicated drift-check helper with a real backend and real var file:

```powershell
powershell -File ./scripts/deploy/Invoke-TaskdeckTerraformDriftCheck.ps1 `
  -Environment staging `
  -VarFile deploy/terraform/aws/environments/staging/terraform.tfvars `
  -BackendConfigFile deploy/terraform/aws/environments/staging/backend.hcl `
  -RefreshOnly
```

Exit contract:

- `0`: no drift detected
- `2`: drift detected
- other: Terraform or configuration failure

The expected operator loop is:

1. run drift check
2. review the plan output
3. reconcile intentional vs unintentional drift
4. apply only after review

## Teardown

Destroy the environment explicitly when appropriate:

```powershell
terraform -chdir=deploy/terraform/aws/environments/dev destroy -var-file=terraform.tfvars
```

For `staging` and `prod`, review bucket retention and data backup posture before destroy.

## Constraints

- This baseline is not a blue/green or canary workflow.
- This baseline does not introduce a managed database.
- This baseline does not solve secrets rotation or provider-credential governance.
- This baseline assumes container images are already available to the host by the references supplied in `terraform.tfvars`.

Use this as the reproducible infrastructure floor under the existing container deployment, not as the final production-topology answer.
