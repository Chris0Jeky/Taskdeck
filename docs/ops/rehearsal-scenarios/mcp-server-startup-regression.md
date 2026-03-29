# Scenario: MCP Server Startup Regression

Last Updated: 2026-03-29
Issue: `#150` OPS-19 incident rehearsal and recovery evidence program

## Overview

Simulate a failure in an optional MCP (Model Context Protocol) server used by CLI/IDE agents during development. The MCP server fails at boot due to a configuration or dependency error. Investigate the failure, determine whether it blocks core API functionality, and recover.

## Pre-Conditions

- Repository checked out at a known commit on `main`.
- MCP server configuration present (`.mcp.json` or equivalent in repo root or `.claude/` directory).
- Node.js / npx available (MCP servers are typically Node-based).
- Backend API is not dependent on MCP servers for core operation (MCP is a development tooling layer).

## Injection Method

### Option A: Invalid MCP Server Command

Modify the MCP server configuration to reference a non-existent command or package.

```bash
# Check current MCP config
cat .mcp.json 2>/dev/null || cat .claude/mcp.json 2>/dev/null || echo "No MCP config found"

# If .mcp.json exists, back it up and inject a fault
cp .mcp.json .mcp.json.bak
# Edit .mcp.json to change a server command to a non-existent binary
# e.g., change "npx" to "npx-nonexistent" for one server entry
```

### Option B: Missing API Key for MCP Server

Some MCP servers require API keys or tokens. Remove or invalidate the required environment variable.

```bash
# Start with an invalid key for a server that requires authentication
GITHUB_PERSONAL_ACCESS_TOKEN="" npx @modelcontextprotocol/server-github
```

### Option C: Port Conflict

If an MCP server binds to a specific port, start a conflicting listener first.

```bash
# Occupy the port before starting the MCP server
python -m http.server 3000 &
# Then attempt to start the MCP server that needs port 3000
```

## Expected Diagnosis Path

1. **Attempt to start the MCP server and capture the error**:
   ```bash
   # Try to start the server using the configured command
   # Capture stderr for error messages
   npx @modelcontextprotocol/server-github 2>&1 | head -20
   ```

2. **Check if the core API is affected**:
   ```bash
   # MCP servers are optional tooling -- verify the API is unaffected
   curl -s http://localhost:5000/health/live | jq .
   curl -s http://localhost:5000/health/ready | jq .
   ```
   Both endpoints should return healthy. MCP server failure must not degrade API health.

3. **Inspect the MCP configuration**:
   ```bash
   cat .mcp.json | jq .
   # Or check the Claude config directory
   ls -la .claude/
   ```

4. **Check for dependency issues**:
   ```bash
   # Verify the MCP server package is resolvable
   npx --yes @modelcontextprotocol/server-github --version 2>&1
   ```

5. **Review MCP tooling guide for known issues**:
   Reference `docs/MCP_TOOLING_GUIDE.md` for the current MCP server status and fallback rules.

## Recovery Steps

### Invalid Command

1. Restore the original MCP configuration:
   ```bash
   mv .mcp.json.bak .mcp.json
   ```
2. Verify the server starts:
   ```bash
   # Test the corrected command
   npx @modelcontextprotocol/server-github --help 2>&1 | head -5
   ```

### Missing API Key

1. Set the required environment variable:
   ```bash
   export GITHUB_PERSONAL_ACCESS_TOKEN="ghp_..."
   ```
2. Restart the MCP server.

### Port Conflict

1. Identify the conflicting process:
   ```bash
   # Linux/Mac
   lsof -i :3000
   # Windows
   netstat -ano | findstr :3000
   ```
2. Stop the conflicting process or reconfigure the MCP server port.

### Fallback: Work Without MCP

Per the MCP Tooling Guide, when MCP is unavailable:
- Use shell/CLI as fallback for the same operations.
- Note the MCP unavailability in the work summary.
- Core development and API operations are unaffected.

## Evidence Checklist

- [ ] Captured error output from the failed MCP server startup
- [ ] Verification that `/health/live` and `/health/ready` are unaffected by MCP failure
- [ ] MCP configuration file contents (redact any tokens or secrets)
- [ ] Diagnosis steps taken to identify the root cause
- [ ] Recovery commands and verification of restored MCP server operation
- [ ] Confirmation that the failure was isolated to development tooling (no production impact)
- [ ] Any findings about MCP error messages (clear vs. cryptic, actionable vs. not)

## Related Documents

- `docs/MCP_TOOLING_GUIDE.md` -- MCP tool selection rules and fallback policy
- `docs/tooling/MCP_OPERATIONS_RUNBOOK.md` -- credential setup and verification
- `.mcp.json` -- MCP server configuration (if present)
