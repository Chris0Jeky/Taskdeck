# Secrets and Configuration Management Baseline

Last Updated: 2026-03-28
Issue: `#110` SEC-10 secrets and configuration management baseline

This document defines the canonical secrets handling policy, rotation model, and operational procedures for all Taskdeck environments.

Related docs:
- `docs/ops/DEPLOYMENT_CONTAINERS.md` -- container deployment baseline
- `docs/ops/DEPLOYMENT_TERRAFORM_BASELINE.md` -- IaC secret handoff mechanics
- `docs/ops/DEPLOYMENT_HARDENING_MATRIX.md` -- compose secret enforcement checks
- `docs/platform/LLM_PROVIDER_SETUP_GUIDE.md` -- LLM provider credential setup
- `docs/security/SECURITY_LOGGING_REDACTION.md` -- logging redaction for sensitive data

## Guiding Principles

1. **No secrets in committed files.** All secret values live outside source control. Committed files contain only placeholder keys with empty values or documentation references.
2. **Least privilege.** Each environment and service receives only the credentials it needs. IAM roles, scoped tokens, and per-environment parameter paths enforce this.
3. **Review-first rotation.** Secret rotation follows an explicit operator-reviewed procedure. Automated rotation is permitted only when the full rollback path is documented and tested.
4. **Auditability.** Every secret access and rotation event must be traceable through infrastructure audit logs (CloudTrail, SSM parameter history, application audit logs).
5. **Safe degradation.** Missing or invalid credentials must cause deterministic, safe failure (e.g., fallback to Mock provider) rather than silent misbehavior.

## Secret Inventory

| Secret | Purpose | Local/Dev | Staging/Prod | Rotation Trigger |
| --- | --- | --- | --- | --- |
| `Jwt:SecretKey` | JWT signing for auth tokens | `appsettings.Development.json` (dev-only placeholder) | AWS SSM SecureString via `jwt_secret_ssm_parameter_name` | Scheduled (90 days) or on compromise |
| `Llm:OpenAi:ApiKey` | OpenAI API access | Environment variable or user secrets | SSM SecureString or CI-injected env var | On compromise or key expiry |
| `Llm:Gemini:ApiKey` | Gemini API access | Environment variable or user secrets | SSM SecureString or CI-injected env var | On compromise or key expiry |
| `TASKDECK_JWT_SECRET` | Compose-level JWT secret injection | `deploy/.env` (untracked) | SSM SecureString pulled at boot | Scheduled (90 days) or on compromise |
| `GITHUB_PAT` | GitHub MCP / CI access | `.env` (untracked, user-local) | GitHub Actions secrets | On compromise or PAT expiry |
| Webhook signing keys | Per-subscription outbound webhook HMAC | Generated at subscription creation | Same (stored in DB, per-subscription) | Explicit rotation via API (`POST /api/outbound-webhooks/{id}/rotate-secret`) |
| Terraform state credentials | Remote state backend access | `backend.hcl` (untracked) | CI-injected `TF_VAR_*` / OIDC | On compromise |

## Environment-Specific Storage Model

### Local Development

- **JWT secret**: Hardcoded dev-only value in `appsettings.Development.json`. This value is intentionally weak and clearly marked; it must never be used outside local dev.
- **LLM API keys**: Not required (Mock provider is default). When needed for live-provider demos, set via environment variables (`Llm__OpenAi__ApiKey`, `Llm__Gemini__ApiKey`) or .NET user secrets (`dotnet user-secrets set "Llm:OpenAi:ApiKey" "<key>"`).
- **GitHub PAT**: Set in user environment or root `.env` (gitignored).
- **No secrets required in committed files.** `appsettings.json` contains only empty placeholder values for optional provider keys.

### Docker Compose (Local Self-Host)

- **JWT secret**: Required in `deploy/.env` (gitignored). Compose will fail to render if `TASKDECK_JWT_SECRET` is empty, enforced by the `${TASKDECK_JWT_SECRET:?...}` guard in `docker-compose.yml`.
- **LLM API keys**: Optional. Pass via `deploy/.env` when live providers are needed.
- Template: `deploy/.env.example` documents all expected variables with empty secret values.

### Staging / Production (Terraform + AWS)

- **JWT secret**: Stored as AWS SSM SecureString parameter. The Terraform module references the parameter by name (`jwt_secret_ssm_parameter_name`) and the EC2 instance role has scoped `ssm:GetParameter` permission. Optional customer-managed KMS key support via `jwt_secret_kms_key_arn`.
- **LLM API keys**: Stored as additional SSM SecureString parameters or injected via the deployment environment file at boot. Not passed through Terraform `user_data` in plaintext.
- **Terraform state credentials**: Passed via untracked `backend.hcl` or CI-injected environment variables. Never committed.

### CI (GitHub Actions)

- **Secrets**: Stored in GitHub repository or environment secrets. Referenced in workflows via `${{ secrets.NAME }}`.
- **Registry tokens**: Scoped CI tokens, never personal passwords.
- **No secrets in workflow files.** All sensitive values are injected at runtime from the GitHub secrets store.

## Committed File Audit

The following committed files contain secret-shaped keys. All values are empty placeholders or dev-only markers:

| File | Key | Value | Status |
| --- | --- | --- | --- |
| `appsettings.json` | `Llm:OpenAi:ApiKey` | `""` (empty) | Safe -- placeholder only |
| `appsettings.json` | `Llm:Gemini:ApiKey` | `""` (empty) | Safe -- placeholder only |
| `appsettings.Development.json` | `Jwt:SecretKey` | Dev-only marker value | Safe -- clearly marked, dev-only |
| `deploy/.env.example` | `TASKDECK_JWT_SECRET` | `""` (empty) | Safe -- template only |
| `.env.example` | `GITHUB_PAT` | `""` (empty) | Safe -- template only |
| `terraform.tfvars.example` (all envs) | `jwt_secret_ssm_parameter_name` | Example path | Safe -- example only |

## Gitignore Coverage

The `.gitignore` excludes all files that could contain real secrets:

- `.env` and `.env.*` (except `.env.example`)
- `frontend/*/.env.local` and `frontend/*/.env.*.local`
- `deploy/terraform/aws/environments/*/terraform.tfvars`
- `deploy/terraform/aws/environments/*/*.auto.tfvars`
- `deploy/terraform/aws/environments/*/backend.hcl`
- `*.tfstate` and `*.tfstate.*`

## Rotation Procedures

### JWT Signing Key Rotation

**Trigger**: Scheduled every 90 days, or immediately on suspected compromise.

**Local/Compose**:
1. Generate a new strong random secret (minimum 32 bytes, e.g., `openssl rand -base64 48`).
2. Update `deploy/.env` with the new `TASKDECK_JWT_SECRET` value.
3. Restart the compose stack. All existing JWTs become invalid (users must re-authenticate).
4. Verify via `/health/ready` and a test auth flow.

**Staging/Prod (SSM)**:
1. Generate a new strong random secret.
2. Update the SSM parameter:
   ```bash
   aws ssm put-parameter \
     --name /taskdeck/<env>/jwt-secret \
     --type SecureString \
     --value "<new-secret>" \
     --overwrite \
     --region <region>
   ```
3. Trigger host replacement (Terraform apply with `user_data_replace_on_change`) or restart the application to pick up the new value.
4. All existing JWTs become invalid. Verify with `/health/ready` and a test auth flow.
5. Confirm rotation in SSM parameter version history for audit trail.

**Rollback**: Re-apply the previous SSM parameter version value and restart. SSM parameter history provides the audit trail.

### LLM Provider API Key Rotation

**Trigger**: On compromise, key expiry, or provider-mandated rotation.

1. Generate a new API key in the provider dashboard (OpenAI / Google AI Studio).
2. Update the key in the target environment:
   - Local: environment variable or `dotnet user-secrets`
   - Compose: `deploy/.env`
   - Staging/Prod: SSM SecureString parameter or deployment env file
3. Restart the application or wait for config reload.
4. Verify via the health probe endpoint (`GET /health/ready?probe=true`) which exercises provider connectivity.
5. Revoke the old key in the provider dashboard only after confirming the new key works.

**Rollback**: Restore the previous key value before revoking it. The provider dashboard retains key history.

### Webhook Signing Key Rotation

**Trigger**: On compromise or subscriber request.

1. Call the rotation API endpoint:
   ```
   POST /api/outbound-webhooks/{subscriptionId}/rotate-secret
   ```
2. The API generates a new signing key and returns it in the response. The old key is immediately invalidated.
3. The subscriber must update their signature verification to use the new key.
4. Verify by triggering a test webhook delivery and confirming signature validation on the subscriber side.

**Rollback**: Not directly possible (old key is invalidated). The subscriber must coordinate with the new key. For planned rotations, use a brief dual-key verification window on the subscriber side.

### GitHub PAT / CI Token Rotation

**Trigger**: On expiry (GitHub fine-grained PATs have configurable expiry) or on compromise.

1. Generate a new PAT in GitHub settings with minimum required scopes.
2. Update the value in the target location:
   - Local: user environment or root `.env`
   - CI: GitHub repository/environment secrets
3. Verify CI workflows execute successfully.
4. Revoke the old PAT after confirming the new one works.

### Terraform State Backend Credentials

**Trigger**: On compromise or credential rotation policy.

1. Rotate credentials in the cloud provider IAM console.
2. Update untracked `backend.hcl` files or CI environment variables.
3. Run `terraform init` to confirm backend connectivity.
4. Verify with `terraform plan` (no unexpected changes).

## CI Validation

### Compose Secret Enforcement

The deployment hardening matrix (`docs/ops/DEPLOYMENT_HARDENING_MATRIX.md`) includes an automated check that `docker compose config` fails when `TASKDECK_JWT_SECRET` is missing. This runs as part of the hardening verification script:

```powershell
powershell -File ./scripts/deploy/Verify-TaskdeckDeploymentHardening.ps1 -Port 8080
```

### Committed Secret Scan

To verify no real secrets are present in committed files, run:

```bash
# Check that all secret-shaped values in config templates are empty or placeholder
grep -rn "ApiKey\|SecretKey\|JWT_SECRET\|GITHUB_PAT" \
  appsettings.json deploy/.env.example .env.example \
  --include="*.json" --include="*.example" \
  | grep -v '""' | grep -v "=\s*$" | grep -v "ChangeMe" | grep -v "example"
```

An empty result confirms no real secrets are committed. This check can be added to CI as a pre-merge gate.

### Provider Config Validation

The `LlmProviderSelectionPolicy` performs runtime validation of provider configuration. If `EnableLiveProviders` is true but the selected provider's `ApiKey` is empty or config is invalid, the system degrades safely to the Mock provider. The `/health/ready?probe=true` endpoint exercises this path.

## Operational Checklist

Use this checklist when bootstrapping a new environment:

- [ ] Generate a strong JWT secret (minimum 32 bytes)
- [ ] Store JWT secret in the appropriate location for the environment tier
- [ ] Verify compose/application startup succeeds with the secret
- [ ] Verify compose fails without the secret (hardening check)
- [ ] If using live LLM providers: store API keys in the appropriate secret store
- [ ] Verify `/health/ready` returns 200
- [ ] If using live providers: verify `/health/ready?probe=true` returns 200
- [ ] Confirm `.gitignore` covers all untracked secret-bearing files
- [ ] Record the bootstrap date and operator in the environment's operational log

## Constraints and Future Work

- **Single JWT signing key**: The current implementation uses a single symmetric key. Key rollover with dual-key support (old + new key accepted during a grace period) is not yet implemented. Rotation requires accepting that all existing sessions are invalidated.
- **No automated rotation**: Rotation is operator-initiated. Automated rotation with health-gated rollback is a future enhancement.
- **No centralized secret manager integration**: Staging/prod use SSM SecureString. Integration with HashiCorp Vault or AWS Secrets Manager (with native rotation support) is a future option.
- **Webhook key dual-acceptance window**: The current webhook rotation immediately invalidates the old key. A dual-key grace period for zero-downtime rotation is a future enhancement.
