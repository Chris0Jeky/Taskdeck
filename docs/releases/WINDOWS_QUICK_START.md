# Taskdeck Windows Quick Start (0.2.x)

Taskdeck 0.2.x supports **Windows 10/11 x64**. Download the Windows ZIP and its
`.sha256` file from the [official Taskdeck Releases page](https://github.com/Chris0Jeky/Taskdeck/releases/latest).
The other archives attached to v0.1.0 are preserved historical artifacts, not a continuing support
promise.

## 1. Verify the download

Open PowerShell in the download folder, set the version you downloaded, and compare the archive with
its checksum file:

```powershell
$version = 'v0.2.0' # change this to the version you downloaded
$zip = "taskdeck-$version-win-x64.zip"
$expected = ((Get-Content "$zip.sha256" -Raw) -split '\s+')[0].ToLowerInvariant()
$actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $zip).Hash.ToLowerInvariant()
if ($actual -ne $expected) { throw 'Taskdeck download checksum mismatch. Do not run this file.' }
'Taskdeck checksum verified.'
```

If the names differ from the example, use the exact downloaded names. Do not continue when the hash
does not match.

## 2. Extract and start

1. Right-click the ZIP and choose **Extract All**. Do not run Taskdeck from inside the ZIP.
2. Keep the extracted files together. Double-click **`Taskdeck.Api.exe`**.
3. Keep the console window open while using Taskdeck. First startup creates the database and applies
   migrations, so it can take longer than a restart.
4. Wait for `Taskdeck is ready at http://127.0.0.1:<port>`. The packaged app opens that address in
   your default browser after the readiness check passes. If no browser opens, copy the exact printed
   address into a browser yourself.

Taskdeck prefers `http://127.0.0.1:5000`. If another program already owns port 5000, the packaged app
selects a free loopback port and prints it; bookmarks for port 5000 are therefore not authoritative.
The listener remains local to this computer.

You may create a shortcut to `Taskdeck.Api.exe`. Its **Start in** directory may be blank or point
somewhere unrelated: the packaged app resolves its web files from the extracted application folder
and its writable state from your Windows profile. Do not copy only the EXE away from the rest of the
extracted folder.

### Windows unsigned-beta warning

The 0.2.x executable is not code-signed. Windows SmartScreen may show **Windows protected your PC**.
Continue with **More info -> Run anyway** only when the ZIP came from the official Taskdeck release
and the SHA-256 check above passed. Do not disable SmartScreen globally. Delete the download and
[report it](https://github.com/Chris0Jeky/Taskdeck/issues) if its source or hash is wrong.

## 3. Register and use the review gate

The release archive does **not** contain a seeded account. Use **Register** to create your local
account. The `demo` / `demo123` credentials belong only to a source checkout started with the seeded
development launcher; they do not work in the packaged release.

Taskdeck's safe path is:

`Inbox/capture -> proposal -> Review -> Approve -> Apply -> confirm Apply`

Approval alone does not change the board. Applying still requires the separate confirmation dialog.

### Optional: connect an MCP client

The release ZIP includes `MCP_SERVER.md`, `mcp.example.json`, and
`mcp-docker.example.json`. After registering the intended local user, open `MCP_SERVER.md` and follow
the packaged Windows instructions for Claude Code, Claude Desktop, or Cursor. The desktop example
must point to the absolute path of this extracted `Taskdeck.Api.exe` and starts it with `--mcp`.

MCP board-changing tools create proposals for Review; they cannot approve or Apply their own
proposals. The automated release gate proves the extracted executable completes protocol
initialization without contaminating stdout, but it is not an external-client or full
agent-proposes/human-applies demo.

## 4. Stop safely

Return to the Taskdeck console and press **Ctrl+C**. Wait for both messages before closing the window:

```text
Taskdeck is stopping safely.
Taskdeck stopped. You can close this window.
```

Closing the console abruptly can leave SQLite `-wal` or `-shm` recovery files. Start Taskdeck and stop
it cleanly before taking a normal backup.

## 5. Data, configuration, backup, and upgrade

The extracted application folder contains the read-only release defaults, including
`appsettings.json`. Writable per-user state is stored outside that folder:

```text
%LOCALAPPDATA%\Taskdeck\taskdeck.db
%LOCALAPPDATA%\Taskdeck\appsettings.local.json
%LOCALAPPDATA%\Taskdeck\backups\
```

The console prints the effective data directory on every packaged start. An explicit absolute
`ConnectionStrings__DefaultConnection` override can move the database; if you use one, back up that
exact path instead.

Before an upgrade:

1. Stop Taskdeck with Ctrl+C and stop any Taskdeck CLI or MCP process using the same database.
2. Copy the entire `%LOCALAPPDATA%\Taskdeck` folder somewhere safe. At minimum keep `taskdeck.db` and
   `appsettings.local.json` together: the JSON file contains generated local identity used to preserve
   sessions and decrypt stored connector credentials. If `taskdeck.db-wal` or `taskdeck.db-shm` remains,
   keep it with the database rather than discarding recovery evidence.
3. Extract the new ZIP to a new folder and run its `Taskdeck.Api.exe`. Do not overwrite the old
   extracted folder while Taskdeck is running.

Pending database migrations run automatically and take a pre-migration snapshot when applicable.
Downgrading a migrated database is not supported. See the online
[upgrade guide](https://github.com/Chris0Jeky/Taskdeck/blob/main/UPGRADING.md) for restore, export, and
version-specific notes.

## 6. Optional OpenAI setup (current PowerShell process only)

Taskdeck is deterministic Mock by default. The supported live provider is OpenAI and its default model
is `gpt-5.6-luna`. To opt in without persisting the key, open a **fresh PowerShell window** in the
extracted folder and run:

```powershell
$env:Llm__EnableLiveProviders = 'true'
$env:Llm__Provider = 'OpenAI'
$secret = Read-Host 'OpenAI API key (input is hidden)' -AsSecureString
$env:Llm__OpenAi__ApiKey = [System.Net.NetworkCredential]::new('', $secret).Password
try {
    .\Taskdeck.Api.exe
}
finally {
    Remove-Item Env:Llm__OpenAi__ApiKey -ErrorAction SilentlyContinue
    Remove-Item Env:Llm__Provider -ErrorAction SilentlyContinue
    Remove-Item Env:Llm__EnableLiveProviders -ErrorAction SilentlyContinue
    $secret.Dispose()
}
```

The command remains blocked while Taskdeck runs; press Ctrl+C so the cleanup in `finally` executes.
These values exist only in that PowerShell process and the Taskdeck child process. Do not use `setx`,
put the key in `appsettings.json`, commit it, paste it into an issue, or include it in screenshots or
evidence. `Llm__AllowLiveProvidersInDevelopment=true` is an additional gate only for a source build
running in Development/Test/Testing; the packaged Production app does not need it.

After registering:

1. Open **Chat** (use Search / Ctrl+K if it is hidden in Guided mode).
2. The status initially means only that configuration parsed. Click **Verify LLM**. A real upstream
   probe consumes tokens; require the result to say **verified**, **OpenAI**, **non-mock**, and
   `gpt-5.6-luna` (unless you deliberately overrode the model).
3. Create a blank board, capture a synthetic instruction such as `Create a card named packaged
   OpenAI check`, and start triage into that board. Inspect the proposal in **Review**. Confirm that
   the board is unchanged after **Approve**, then use **Apply to board** and the separate confirm-Apply
   dialog only if you want the synthetic card to be created.

A proposal by itself is **not** proof that OpenAI was called: Mock and deterministic fallback paths can
also produce proposals. `configured` is not `verified`; `mock`, `unavailable`, `error`, or a degraded
fallback means live reachability was not proven. Taskdeck's provider transport connects directly and
does not use an ambient corporate proxy, so proxy-only networks fail closed.

An OpenAI provider key is unrelated to a Taskdeck/MCP API key. Taskdeck keys begin with `tdsk_`, are
created inside Taskdeck, and authenticate local API/MCP clients; never put a `tdsk_` value in
`Llm__OpenAi__ApiKey` or send an OpenAI key as a Taskdeck bearer token.

## Troubleshooting and support

- No browser: use the exact `Taskdeck is ready at ...` URL printed in the console.
- Port 5000 busy: use the printed fallback URL. Stop the other listener only if you recognize it.
- Startup fails with `TASKDECK_DESKTOP_FATAL code=startup_failed` (v0.1.1 only): if this
  machine ever configured the retired Gemini provider through user-scoped environment variables
  (`Llm__Provider=Gemini` or `Llm__Gemini__*`), v0.1.1 inherits them, exits before listening,
  and wrongly suggests a port/data-folder problem. Follow the provider-migration workaround in
  [UPGRADING.md](../../UPGRADING.md#version-notes). v0.1.2 and later print the accurate
  diagnostic below instead.
- **From v0.3.0-rc.1 onward** — `TASKDECK_DESKTOP_WARNING code=retired_provider_configuration_ignored`: Taskdeck started normally after ignoring retired Gemini settings inherited from this machine's environment (environment variables only — a retired selector passed on the command line still fails closed); no retired value was kept, logged, or printed, and the provider actually in use is the one shown in Taskdeck's provider status. Clear those leftover variables with the commands below when convenient. **The published v0.2.0 archive does not have this behaviour**: there the same leftover variables produce the fatal below, and the commands are a required workaround rather than optional tidying.
- `TASKDECK_DESKTOP_FATAL code=retired_provider_configuration`: Taskdeck found configuration for the
  retired Gemini provider and refused to switch providers silently. **In v0.2.0 and earlier this
  fires for retired Gemini settings from any source, including leftover `Llm__Gemini__*` variables
  in your Windows profile — run the commands below before starting Taskdeck again. From
  v0.3.0-rc.1 the only source that stops being fatal is the process environment;** retired
  settings from every other source still fail closed, including Taskdeck's own
  `appsettings.json` / `appsettings.local.json`, Docker Compose, and a command-line argument
  such as `--Llm:Provider=Gemini`. Close Taskdeck, then use a fresh
  PowerShell window to explicitly return **User**-scoped configuration to deterministic Mock and
  remove only the retired child-setting names; the commands do not read or print their values:

  ```powershell
  [Environment]::SetEnvironmentVariable('Llm__Provider', 'Mock', 'User')
  @([Environment]::GetEnvironmentVariables('User').Keys) |
      ForEach-Object { [string]$_ } |
      Where-Object { $_ -like 'Llm__Gemini__*' } |
      ForEach-Object { [Environment]::SetEnvironmentVariable($_, $null, 'User') }
  ```

  Close that PowerShell window before starting Taskdeck again so the new process receives the
  updated environment. If the failure came from Docker Compose, remove the retired Gemini wrapper
  variable from the Compose environment and explicitly select OpenAI, OpenAICompatible, Ollama, or
  Mock instead. A stale Gemini child section is inert after an explicit supported provider is set,
  so migration can be completed without exposing or copying the retired values.
- Other startup failure: keep the console open, confirm `%LOCALAPPDATA%\Taskdeck` is writable, and
  preserve any named database/config recovery files before changing them.
- Product or packaging problem: search or open a report in
  [Taskdeck Issues](https://github.com/Chris0Jeky/Taskdeck/issues). Never include secrets or private
  workspace content.
- Security concern: follow the [private security-reporting policy](https://github.com/Chris0Jeky/Taskdeck/security/policy)
  instead of opening a public issue.
