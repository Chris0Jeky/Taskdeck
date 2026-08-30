# MCP Operations Runbook

Last Updated: 2026-08-30
Scope: Local operator setup, credential wiring, verification, and routine usage for Taskdeck MCP integrations.

## Purpose

This runbook is the operational companion to `docs/MCP_TOOLING_GUIDE.md`.
- `MCP_TOOLING_GUIDE.md` explains **which tool to use**.
- This runbook explains **how to set it up, verify it, and use it repeatedly**.

## Baseline Topology

The Docker MCP gateway is declared **once, at user scope** — never in `.codex/config.toml` or `.mcp.json`,
where a second declaration starts a second gateway process per session (agent-harness#87). Setup path:
- Claude: `mcpServers.MCP_DOCKER` in `~/.claude.json`
- Codex: `[mcp_servers.MCP_DOCKER]` in `~/.codex/config.toml`
- Both run `docker mcp gateway run --watch=false --servers docker,docker-docs,time,jetbrains,filesystem,SQLite --transport stdio`

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

### Baseline profile

```powershell
powershell -File ./scripts/mcp/Test-DockerMcpProfile.ps1
```

This is a **non-starting validation**. It parses `docker mcp profile server ls --format json`, requires
every requested server name to be present, and compares the exact set of `docker-mcp=true` container
IDs before and after the read-only checks. The script withholds `PASS` if either snapshot cannot be
read or if any ID is added or removed. It never starts a gateway and never stops or removes a
container. A changed set may belong to another concurrent gateway, so set difference alone is not
safe ownership evidence.

The check proves local profile membership, prerequisite presence, and container-state neutrality. It
does not prove remote credential acceptance or MCP tool execution. Exercise those only through the
already configured user-scope client session when runtime assurance is required; do not start a
second gateway as a validation probe.

### Optional integrations (checks credential entries and profile membership)

```powershell
powershell -File ./scripts/mcp/Test-DockerMcpProfile.ps1 -IncludeOptional
powershell -File ./scripts/mcp/Test-DockerMcpProfile.ps1 -IncludeOptional -FailOnOptionalErrors
```

Pass/fail policy for optional integrations:
- warning mode: `-IncludeOptional`
  - missing prerequisites and absent optional profile members are reported as warnings
  - script exits success so baseline automation can continue
- strict mode: `-IncludeOptional -FailOnOptionalErrors`
  - missing prerequisites or absent optional profile members fail with non-zero exit
- warning mode with prerequisite skip: `-IncludeOptional -SkipOptionalWhenMissingPrereqs`
  - when prerequisite checks fail, optional membership validation is skipped and a warning is emitted

### Direct inspection commands

```powershell
docker mcp profile server ls --format json
docker mcp secret ls
docker ps --all --quiet --no-trunc --filter "label=docker-mcp=true"
powershell -File ./scripts/mcp/Test-DockerMcpProfile.ps1 -CiMode
```

Do not use `docker mcp gateway run --dry-run` as a profile check. Docker documents that flag as
starting the gateway without a listener, and affected versions can leave server containers running.
The CLI exposes no invocation label or container-ID file, so a validator cannot safely claim or clean
up containers merely because they appeared after a command began.

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
- `MCP_PROFILE_PROBE=READ_ONLY_PROFILE`
- `MCP_PROFILE_RESULT=PASS`
- `MCP_PROFILE_RESULT=PASS_WITH_WARNINGS`
- `MCP_PROFILE_RESULT=FAIL`
- before/after container counts and SHA-256 identity-set fingerprints
- added/removed identity counts (both zero for `PASS`)

## Weekly Workflow

1. Confirm optional credential entries are still present (postman/dockerhub).
2. Re-run optional read-only profile validation.
3. Reconcile docs if server set, config paths, or credential expectations changed.
4. Seed/refresh hardening/testing issues for MCP + deployment reliability.

## Troubleshooting

`postman` profile validation fails:
- Verify `postman.postman-api-key` exists in `docker mcp secret ls`.
- Rotate API key and re-run setup script.

`dockerhub` profile validation fails:
- Verify both `dockerhub.pat_token` and `dockerhub.username`.
- Confirm PAT scope is sufficient for Docker Hub API operations.

`kubernetes` fails to initialize:
- Verify `kubernetes.config_path` points to a real kubeconfig with a valid context.

Container-state drift is reported:
- Do not remove the reported or pre-existing containers unless their ownership is independently proven.
- Inspect concurrent user-scope MCP sessions, then rerun the read-only validator once the state is stable.

## Security Notes

- Never commit secrets in repo files, scripts, or PR bodies.
- Keep secrets only in Docker MCP secret store or ephemeral shell env vars.
- Prefer short-lived or scoped tokens where supported.
- Rotate credentials immediately if exposed in logs/shell history.

## Related

- `docs/MCP_TOOLING_GUIDE.md`
- `docs/ops/DEPLOYMENT_CONTAINERS.md`
- `scripts/mcp/Set-MarketplaceMcpCredentials.ps1`
- `scripts/mcp/Test-DockerMcpProfile.ps1`
- Issue: `#140`

