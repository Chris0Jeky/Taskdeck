# MCP Operations Runbook

Last Updated: 2026-02-21  
Scope: Local operator setup, credential wiring, verification, and routine usage for Taskdeck MCP integrations.

## Purpose

This runbook is the operational companion to `docs/MCP_TOOLING_GUIDE.md`.
- `MCP_TOOLING_GUIDE.md` explains **which tool to use**.
- This runbook explains **how to set it up, verify it, and use it repeatedly**.

## Baseline Topology

Project default Docker MCP gateway server set (from `.codex/config.toml`):
- `docker,docker-docs,openapi,time,jetbrains,filesystem,SQLite,terraform`

Optional enabled Docker Marketplace servers (credential/config gated):
- `postman`
- `dockerhub`
- `kubernetes`
- `semgrep`

## Credential Matrix

| Server | Required Inputs | Where Config Lives |
|---|---|---|
| `postman` | `postman.postman-api-key` secret | Docker MCP secret store |
| `dockerhub` | `dockerhub.username` + `dockerhub.pat_token` secret | `~/.docker/mcp/config.yaml` + Docker MCP secret store |
| `kubernetes` | `kubernetes.config_path` | `~/.docker/mcp/config.yaml` |
| `semgrep` | Semgrep account/auth (remote) | Remote provider |

## One-Time Credential Wiring

### Recommended (scripted)

Use environment variables in the current shell session:

```powershell
$env:POSTMAN_API_KEY = '<your_postman_api_key>'
$env:DOCKERHUB_USERNAME = '<your_dockerhub_username>'
$env:HUB_PAT_TOKEN = '<your_dockerhub_pat>'
powershell -File ./scripts/mcp/Set-MarketplaceMcpCredentials.ps1 -UseEnvironment -Verify
```

Or pass explicit arguments:

```powershell
powershell -File ./scripts/mcp/Set-MarketplaceMcpCredentials.ps1 `
  -PostmanApiKey '<your_postman_api_key>' `
  -DockerHubUsername '<your_dockerhub_username>' `
  -DockerHubPatToken '<your_dockerhub_pat>' `
  -Verify
```

### Manual (without helper script)

```powershell
echo '<your_postman_api_key>' | docker mcp secret set postman.postman-api-key
echo '<your_dockerhub_pat>' | docker mcp secret set dockerhub.pat_token
```

Then set `dockerhub.username` in:
- `C:\Users\<you>\.docker\mcp\config.yaml`

Example:

```yaml
dockerhub:
  username: '<your_dockerhub_username>'
```

## Verification

### Baseline profile (always expected to pass)

```powershell
powershell -File ./scripts/mcp/Test-DockerMcpProfile.ps1
```

### Optional integrations (requires valid credentials)

```powershell
powershell -File ./scripts/mcp/Test-DockerMcpProfile.ps1 -IncludeOptional
powershell -File ./scripts/mcp/Test-DockerMcpProfile.ps1 -IncludeOptional -FailOnOptionalErrors
```

Pass/fail policy for optional integrations:
- warning mode: `-IncludeOptional`
  - missing prerequisites and optional runtime failures are reported as warnings
  - script exits success so baseline automation can continue
- strict mode: `-IncludeOptional -FailOnOptionalErrors`
  - missing prerequisites or optional runtime failures fail fast with non-zero exit
- warning mode with prerequisite skip: `-IncludeOptional -SkipOptionalWhenMissingPrereqs`
  - when prerequisite checks fail, optional dry-run is skipped and warning is emitted

### Direct inspection commands

```powershell
docker mcp server ls
docker mcp secret ls
docker mcp gateway run --dry-run --servers docker,docker-docs,openapi,time,jetbrains,filesystem,SQLite,terraform
docker mcp gateway run --dry-run --servers postman,dockerhub
```

## Daily Workflow

Start of session:
1. Ensure Docker Desktop is running.
2. Run baseline MCP profile check.
3. If working with Postman or Docker Hub MCP, run optional check.

Before opening a PR that touches ops/MCP/deployment:
1. Run docs governance checks.
2. Run baseline MCP profile check.
3. If optional MCP paths were changed, run optional MCP profile check.

Commands:

```powershell
node scripts/check-docs-governance.mjs
node scripts/check-github-ops-governance.mjs
powershell -File ./scripts/mcp/Test-DockerMcpProfile.ps1
```

CI-friendly command patterns:

```powershell
# baseline only (strict)
powershell -File ./scripts/mcp/Test-DockerMcpProfile.ps1 -CiMode

# optional best-effort (warn + skip when prerequisites are missing)
powershell -File ./scripts/mcp/Test-DockerMcpProfile.ps1 -IncludeOptional -SkipOptionalWhenMissingPrereqs -CiMode

# optional strict gate (fail on missing prereqs or runtime failures)
powershell -File ./scripts/mcp/Test-DockerMcpProfile.ps1 -IncludeOptional -FailOnOptionalErrors -CiMode
```

`-CiMode` outputs deterministic status lines:
- `MCP_PROFILE_RESULT=PASS`
- `MCP_PROFILE_RESULT=PASS_WITH_WARNINGS`
- `MCP_PROFILE_RESULT=FAIL`

## Weekly Workflow

1. Confirm optional credentials are still valid (postman/dockerhub).
2. Re-run optional MCP dry-run.
3. Reconcile docs if server set, config paths, or credential expectations changed.
4. Seed/refresh hardening/testing issues for MCP + deployment reliability.

## Troubleshooting

`postman` dry-run fails:
- Verify `postman.postman-api-key` exists in `docker mcp secret ls`.
- Rotate API key and re-run setup script.

`dockerhub` dry-run fails:
- Verify both `dockerhub.pat_token` and `dockerhub.username`.
- Confirm PAT scope is sufficient for Docker Hub API operations.

`kubernetes` fails to initialize:
- Verify `kubernetes.config_path` points to a real kubeconfig with a valid context.

Docker MCP gateway slow start:
- This is expected when many images are pulled the first time.
- Re-run the same command; subsequent runs should be faster.

## Security Notes

- Never commit secrets in repo files, scripts, or PR bodies.
- Keep secrets only in Docker MCP secret store or ephemeral shell env vars.
- Prefer short-lived or scoped tokens where supported.
- Rotate credentials immediately if exposed in logs/shell history.

## Related

- `docs/MCP_TOOLING_GUIDE.md`
- `docs/DEPLOYMENT_CONTAINERS.md`
- `scripts/mcp/Set-MarketplaceMcpCredentials.ps1`
- `scripts/mcp/Test-DockerMcpProfile.ps1`
- Issue: `#140`

