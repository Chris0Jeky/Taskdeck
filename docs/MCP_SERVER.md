# Taskdeck MCP Server

Taskdeck exposes its local-first workspace to MCP clients without giving an agent an approve or
Apply path. Read tools inspect Taskdeck state. Board-changing tools create proposals for human
review; they do not mutate a board directly. A few bounded workflow actions are direct writes:
`create_capture` writes only to Inbox, and `dismiss_proposal` cannot dismiss a pending proposal.

This guide covers the local stdio transport for the packaged Windows executable, the released
Docker image, and a source checkout. Standalone and co-hosted Streamable HTTP remain available for
API-key clients, but the examples here keep one active local stdio server per configuration.

The desktop release ZIP places this guide, `mcp.example.json`, and
`mcp-docker.example.json` side by side. In a source checkout, the two example configurations live in
the repository root.

## Identity and data prerequisites

Stdio has no network authentication. Taskdeck therefore resolves the caller to an active local
Taskdeck user:

- When `McpServer:DefaultUserId` is unset, exactly one active user must exist.
- When more than one active user exists, set `McpServer__DefaultUserId` to the intended active
  user's non-empty GUID in the MCP server environment.
- A configured empty, zero, malformed, missing, or inactive ID fails closed. Taskdeck never falls
  back to a different account.

Start Taskdeck in its normal web mode first and register the intended local user. Reuse the same
database and local configuration for the MCP process. The web app can remain open so proposals
created through MCP can be reviewed and explicitly applied there.

Do not put passwords, Taskdeck API keys, OpenAI keys, or other secrets in a project-scoped MCP file.
The desktop stdio path normally needs no secret in client configuration because it loads the local
configuration created by the packaged web app. A headless Docker or source setup must pass its
existing connector encryption key through its normal private environment file.

## Packaged Windows desktop

1. Download, verify, and extract the Windows ZIP as described in `QUICK_START.md` (online source:
   `docs/releases/WINDOWS_QUICK_START.md`).
2. Run `Taskdeck.Api.exe` normally and register one local user.
3. Copy `mcp.example.json` into your client's configuration.
4. Replace `C:\\REPLACE_WITH_YOUR_TASKDECK_FOLDER\\Taskdeck.Api.exe` with the absolute path to the
   executable in the extracted folder. Keep `args` exactly `["--mcp"]`.

The effective launch command is:

```powershell
& 'C:\absolute\path\to\Taskdeck.Api.exe' --mcp
```

The MCP client owns this process. Closing or disconnecting the client closes stdin and stops the
stdio server. Stdout is reserved for JSON-RPC; Taskdeck sends runtime logs to stderr.

The packaged web and stdio processes both use:

```text
%LOCALAPPDATA%\Taskdeck\taskdeck.db
%LOCALAPPDATA%\Taskdeck\appsettings.local.json
```

## Released Docker image

Use a pinned released version, not an unreviewed local alias. First initialize the named volume
through the image's normal web startup and register the local user at `http://127.0.0.1:5000`:

```bash
docker volume create taskdeck-data
docker run --name taskdeck-web --rm -d -p 5000:5000 \
  --env-file /absolute/path/to/taskdeck-mcp.env \
  --mount source=taskdeck-data,target=/app/data \
  ghcr.io/chris0jeky/taskdeck:REPLACE_WITH_RELEASE_VERSION
```

The private environment file must contain the same strong values used for every restart:

```dotenv
Jwt__SecretKey=REPLACE_WITH_A_STRONG_RANDOM_SECRET
Connectors__EncryptionKey=REPLACE_WITH_A_BASE64_32_BYTE_KEY
```

After registration, copy `mcp-docker.example.json` into the MCP client's configuration. Replace the
environment-file path and image version, but keep the named volume identical to the web container.
Its effective stdio command is:

```bash
docker run --rm -i --no-healthcheck \
  --user 1001:1001 \
  --env-file /absolute/path/to/taskdeck-mcp.env \
  --mount source=taskdeck-data,target=/app/data \
  ghcr.io/chris0jeky/taskdeck:REPLACE_WITH_RELEASE_VERSION \
  dotnet Taskdeck.Api.dll --mcp
```

These launch details are required:

- `--no-healthcheck` disables the image's HTTP readiness probe because stdio mode intentionally
  opens no HTTP listener.
- `--user 1001:1001` uses the image's non-root Taskdeck account after normal web startup has
  initialized ownership of the named volume. It also keeps the privilege wrapper's root-mode
  informational message off the JSON-RPC stdout channel. Do not skip the normal startup and user
  registration steps above.
- `dotnet Taskdeck.Api.dll --mcp` overrides the image's normal web command. The image entrypoint is
  only the privilege-dropping data-directory wrapper, so `IMAGE --mcp` is not a valid invocation.

Stop the setup container when it is no longer needed with `docker stop taskdeck-web`. Do not remove
the `taskdeck-data` volume unless you intend to delete that Taskdeck workspace.

## From source

Run the web app once and create the local user, then point a stdio entry at an absolute project path
and the exact database used by that web process:

```json
{
  "mcpServers": {
    "taskdeck": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "C:\\absolute\\path\\to\\Taskdeck\\backend\\src\\Taskdeck.Api\\Taskdeck.Api.csproj",
        "--",
        "--mcp"
      ],
      "env": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "ConnectionStrings__DefaultConnection": "Data Source=C:\\absolute\\path\\to\\taskdeck-dev.db"
      }
    }
  }
}
```

`scripts/dev-up.ps1` and `scripts/dev-up.sh` print their stable database path. Their environment
override belongs only to the web process they launch, so copy that absolute path into the MCP entry.
The seeded demo path creates more than one active user; add `McpServer__DefaultUserId` for the user
you intend when using that data.

## Client setup

Use one of the two example files as the server entry. Do not combine the desktop and Docker entries;
they would start two Taskdeck MCP processes under the same server purpose.

### Claude Code

For a private, user-scoped packaged-desktop entry on Windows:

```powershell
claude mcp add --transport stdio --scope user taskdeck -- 'C:\absolute\path\to\Taskdeck.Api.exe' --mcp
claude mcp get taskdeck
```

Alternatively, use the example as a project-root `.mcp.json`; Claude Code asks for approval before
using a project-scoped server. Run `/mcp` in Claude Code to inspect connection status. See the
[official Claude Code MCP guide](https://code.claude.com/docs/en/mcp).

### Claude Desktop

On Windows, merge the chosen `mcpServers.taskdeck` entry into:

```text
%APPDATA%\Claude\claude_desktop_config.json
```

Use an absolute executable or environment-file path. Fully quit Claude Desktop, including its tray
process, then reopen it. The [official MCP host guide](https://py.sdk.modelcontextprotocol.io/get-started/real-host/)
documents the configuration location and local stdio lifecycle.

### Cursor

Put the chosen entry in either:

```text
%USERPROFILE%\.cursor\mcp.json       # personal, all projects
<project>\.cursor\mcp.json           # shared with that project
```

Keep `"type": "stdio"`, the absolute paths, and the argument array. Open Cursor's MCP settings to
inspect connection status. See the [official Cursor MCP guide](https://cursor.com/docs/mcp).

## Verify without granting extra authority

After the client reports the server connected:

1. List Taskdeck's tools and resources.
2. Read `taskdeck://boards` and confirm it shows only the intended user's reachable boards.
3. If you test a board-changing tool, use synthetic content and confirm the result is a proposal.
4. Open Taskdeck Review and confirm the board is unchanged until a human approves and separately
   applies the proposal.

The packaged release gate starts the extracted executable with `--mcp`, sends an MCP 2025-11-25
`initialize` request, requires a valid `result.serverInfo`, and rejects any non-protocol stdout. That
automated gate proves packaged stdio startup and framing; it is not a Claude Code, Claude Desktop, or
Cursor end-to-end test, and it does not prove the separate agent-proposes/human-applies demo.

## Troubleshooting

- **No active users:** start the normal web app and register or reactivate the intended user.
- **Multiple active users:** set `McpServer__DefaultUserId` in JSON configuration, or
  `McpServer__DefaultUserId` in an environment file, to the intended active user GUID.
- **Configured user rejected:** remove or correct the value; Taskdeck will not substitute another
  user while a value is present.
- **Client cannot start the process:** use an absolute executable/environment-file path and verify
  it outside the client without adding secrets to the command line.
- **Docker becomes unhealthy:** confirm the MCP entry includes `--no-healthcheck` before the image.
- **Docker opens the web app instead:** confirm the arguments after the image are exactly
  `dotnet`, `Taskdeck.Api.dll`, `--mcp`.
- **Unexpected text on stdout:** treat it as a protocol failure. Application diagnostics belong on
  stderr so stdout remains JSON-RPC only.

For standalone or co-hosted HTTP, create a Taskdeck API key in **Settings -> API Keys** and follow
the repository's `mcp-claude-code-http.example.json`. Every new key requires at least one capability:
**Read** exposes resources and read-only tools, **Propose** exposes the five proposal-producing board
mutation tools, and **Manage** exposes direct capture creation and proposal dismissal. The same scope
checks filter discovery and invocation, and invalid persisted scope masks fail authentication rather
than widening access. Keys migrated from an earlier release are backfilled to **Full** (all three
capabilities) to preserve existing integrations; rotate them with only the capabilities each client
needs when practical. Stdio does not use an API key and receives Full only after its local user
identity resolves successfully. Neither transport exposes approve/apply tools or runtime tool-hash
approval.
