# LAN Device Access Guide — reach a local Taskdeck from a phone

Last Updated: 2026-08-29

Purpose: give a maintainer a reversible, temporary way to open a locally running Taskdeck to one
phone on the same Wi-Fi, so that human-only real-device checks (today: `#1821`, the software-keyboard
verification of the `TdDialog` / `CardModal` visual-viewport binding) can actually be performed.
Taskdeck's shipped posture is loopback-only — `docs/releases/WINDOWS_QUICK_START.md` states "The
listener remains local to this computer", and that stays true of a normal launch. Everything below is
a **testing posture**: one environment variable, one firewall rule, both removed in the teardown step.
This guide does not describe a deployment; for a real shared instance use
`docs/platform/SELF_HOST_TUNNEL_GUIDE.md`, which gives you HTTPS.

## Prerequisites

- Windows 11 host, PowerShell. Taskdeck's PowerShell rule applies: no `&&` chaining — use `;` and
  check `$LASTEXITCODE`.
- One admin PowerShell window (only the firewall step needs it).
- Phone on the **same** Wi-Fi as the host, with no client/AP isolation and no VPN active on either
  device.
- Path A needs an extracted Taskdeck release folder (latest tag: `v0.1.2`). Path B needs a source
  checkout, the .NET 8 SDK, and Node 24.x.
- A trusted network. Plain HTTP with no TLS; do not do this on public/shared Wi-Fi.

### Read the host's LAN IPv4

```powershell
Get-NetIPAddress -AddressFamily IPv4 |
  Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |
  Select-Object InterfaceAlias, IPAddress
```

Measured on this host on 2026-08-29 this returns **three** rows, two of which are Hyper-V/WSL virtual
switches:

```
InterfaceAlias                        IPAddress
vEthernet (WSL (Hyper-V firewall))    172.19.48.1
vEthernet (Default Switch)            172.19.224.1
WiFi 3                                192.168.50.100
```

Take the row whose `InterfaceAlias` is the real Wi-Fi or Ethernet adapter — here `WiFi 3` →
`192.168.50.100`. A `vEthernet (...)` address is not reachable from the phone. This guide writes that
value as `<HOST_IP>`.

### Read the network category (this decides the firewall step)

```powershell
Get-NetConnectionProfile | Select-Object Name, InterfaceAlias, NetworkCategory
```

Measured on this host on 2026-08-29: the Wi-Fi adapter's `NetworkCategory` is **`Public`**, not
`Private`. A firewall rule scoped `-Profile Private` would therefore not apply and the phone would be
blocked with no error anywhere. See the firewall step for the two ways to handle this.

## Path A (recommended) — the packaged desktop executable

The packaged `Taskdeck.Api.exe` serves the SPA itself from `wwwroot/`, built with
`VITE_API_BASE_URL=/api`, so the phone talks to a **single same-origin port**: one listener, no CORS
entry, no frontend rebuild, no second terminal.

1. Stop any running Taskdeck.
2. In the **extracted** release folder (not inside the ZIP), start it with an explicit wildcard listen
   URL. Use `0.0.0.0` literally — see caveats for why `+`, `*`, and a bare LAN IP all fail:

   ```powershell
   Remove-Item Env:TASKDECK_HEADLESS -ErrorAction SilentlyContinue
   $env:ASPNETCORE_URLS = 'http://0.0.0.0:5000'
   .\Taskdeck.Api.exe
   ```

3. Expect this on the console:

   ```
   TASKDECK_DESKTOP_READY url=http://127.0.0.1:5000
   Taskdeck is ready at http://127.0.0.1:5000
   ```

   **The `127.0.0.1` in that line is expected and is not evidence the LAN bind failed.** The packaged
   app deliberately maps a wildcard listen address back to `127.0.0.1` before printing the
   user-facing URL and before opening the local browser. The socket is still bound to every
   interface.
4. Do the firewall step below for TCP **5000**, then go to "Verify from the phone".

Notes:

- Do **not** set `TASKDECK_HEADLESS`. It is not needed for a LAN bind and it moves both
  `appsettings.local.json` **and the SQLite database** from `%LOCALAPPDATA%\Taskdeck` to the
  executable's own folder — a different account and a different board set from your normal desktop
  runs.
- Setting `ASPNETCORE_URLS` also switches off the packaged free-port picker. If 5000 is already
  taken, the app fails to start instead of choosing another port; pick a different port in
  `ASPNETCORE_URLS` and open that port in the firewall step instead.
- The release archive contains **no seeded account**. Registration is open by default, so use
  **Register** on the phone or the host. `demo` / `demo123` exist only in a source checkout that has
  been seeded by the demo harness.

### Path A variant — package current `main` instead of the release ZIP

Only needed if the check must run against current `main` rather than `v0.1.2`. Not rehearsed; the
commands mirror `.github/workflows/release-desktop.yml`.

```powershell
cd frontend/taskdeck-web
npm ci
$env:VITE_API_BASE_URL = '/api'
npx vite build
cd ../..
dotnet publish backend/src/Taskdeck.Api/Taskdeck.Api.csproj -c Release -r win-x64 --self-contained true -p:TaskdeckDesktopPackage=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o publish/win-x64
New-Item -ItemType Directory -Force publish/win-x64/wwwroot | Out-Null
Copy-Item frontend/taskdeck-web/dist/* publish/win-x64/wwwroot/ -Recurse -Force
```

Then run `publish\win-x64\Taskdeck.Api.exe` exactly as in Path A.

## Path B — dev servers from a source checkout

Two listeners, two ports, one CORS entry. Use this when the check must run against the current
working tree.

1. **Terminal 1 — API on all interfaces.** This mirrors the invocation `scripts/dev-up.ps1` already
   uses (`--no-launch-profile` plus an explicit `--urls`, with the environment set by hand because
   `launchSettings.json` is bypassed):

   ```powershell
   $env:ASPNETCORE_ENVIRONMENT = 'Development'
   $env:Cors__DevelopmentAllowedOrigins__0 = 'http://<HOST_IP>:5173'
   dotnet run --no-launch-profile --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj --urls "http://0.0.0.0:5000"
   ```

   The configuration key is **`Cors:DevelopmentAllowedOrigins`** (environment form
   `Cors__DevelopmentAllowedOrigins__0`, or a comma-separated
   `$env:Cors__DevelopmentAllowedOrigins = 'http://<HOST_IP>:5173'`). `Cors:AllowedOrigins` is the
   **production** key and is ignored while the environment is `Development`. Origins must be absolute
   `http`/`https` origins or startup throws.

2. **Terminal 2 — Vite on all interfaces, pointed at the host's LAN IP.** The SPA has **no dev
   proxy**: it calls the API directly at the build-time `VITE_API_BASE_URL` (default
   `http://localhost:5000/api`), which on the phone would mean *the phone's own* localhost. The
   SignalR hub URL is derived from the same value.

   ```powershell
   $env:VITE_API_BASE_URL = 'http://<HOST_IP>:5000/api'
   cd frontend/taskdeck-web
   npm run dev -- --host
   ```

   `--host` with no value binds `0.0.0.0` (equivalently `$env:TASKDECK_DEV_HOST = '0.0.0.0'`). The dev
   launcher picks 5173, falling back to 4173 then 5001, and uses `strictPort`, so it fails rather than
   silently moving. **Read the port it prints** and make the CORS origin and the firewall rule match
   it. If the frontend does not pick up the environment variable, write
   `frontend/taskdeck-web/.env.local` with `VITE_API_BASE_URL=http://<HOST_IP>:5000/api` instead (it
   is gitignored — delete it in teardown).

3. Do the firewall step for **both** TCP 5000 and TCP 5173 (or whichever port Vite printed), then go
   to "Verify from the phone". The phone browses to the **Vite** port.

## Firewall — open the port(s)

Run in an **admin** PowerShell. Because this host's Wi-Fi is classified `Public` (see prerequisites),
scope the rule to both profiles and to the local subnet rather than to `Private` alone:

```powershell
New-NetFirewallRule -DisplayName 'Taskdeck LAN test 5000' -Direction Inbound -Protocol TCP -LocalPort 5000 -Action Allow -Profile Private,Public -RemoteAddress LocalSubnet
# Path B only — match the port Vite actually printed:
New-NetFirewallRule -DisplayName 'Taskdeck LAN test 5173' -Direction Inbound -Protocol TCP -LocalPort 5173 -Action Allow -Profile Private,Public -RemoteAddress LocalSubnet
```

Alternative, if you would rather reclassify the network than widen the rule: set the adapter to
Private once (admin), then use `-Profile Private` in the rules above.

```powershell
Set-NetConnectionProfile -InterfaceAlias 'WiFi 3' -NetworkCategory Private
```

Reclassifying changes Windows-wide discovery/sharing behaviour for that network and is not undone by
this guide's teardown — prefer the `-Profile Private,Public -RemoteAddress LocalSubnet` rule unless
you want the network Private permanently.

## Verify from the phone

1. On the phone, open:
   - Path A: `http://<HOST_IP>:5000/`
   - Path B: `http://<HOST_IP>:5173/`
2. Log in, or **Register** if this data root has no account yet.
3. If the page never loads, work down this list: wrong adapter IP (a `vEthernet` address); phone on
   guest Wi-Fi or client isolation; firewall rule scoped to a profile the active network is not in;
   the listener still on loopback (re-check that `ASPNETCORE_URLS` / `--urls` was actually set in the
   window that started the process); third-party AV firewall.
4. If the page loads but every API call fails in Path B, it is the CORS origin or
   `VITE_API_BASE_URL` — both must carry the exact `<HOST_IP>` and port.
5. Expect **no PWA install prompt and no offline mode**. `http://<HOST_IP>` is not a secure context
   (only `localhost` / `127.0.0.1` are exempt), so the service worker never registers. That is normal
   here and does not affect the `#1821` check.

## The `#1821` real-phone keyboard check

`#1821` asks for exactly this: a real phone, a real software keyboard, open a card, trigger a nested
confirmation, and confirm the footer actions stay reachable — including a browser without
`visualViewport` support, which falls through to the `@supports (height: 100dvh)` path. Run in
portrait, at a width under the 640 px mobile breakpoint.

1. **Record the environment.** In the phone browser's console or via a scratch page, evaluate
   `'visualViewport' in window` and note `true`/`false` along with the OS and browser versions. Do
   the pass at least once with `true` (e.g. iOS Safari) and, if you can find one, once with `false`.
2. **Card modal, keyboard up.** Open a board, open a card (the `Edit Card` modal), tap into the title
   or description field so the keyboard is up. Confirm the footer actions **Save Changes**, **Cancel**
   and **Delete Card** are all fully on screen and tappable above the keyboard.
3. **Nested confirmation — the `#1821` case.** With the card modal open, tap **Delete Card**. The
   nested `Delete Card` dialog (a `TdDialog`, teleported to `<body>`) opens. Confirm its **Cancel**
   and **Delete** buttons are fully visible and tappable, and that the sheet does not extend under the
   keyboard. If the keyboard closes when the dialog opens because focus moved, record that — then
   re-open the keyboard if the browser allows and re-check.
4. **`TdDialog` with the keyboard genuinely up.** Go to Review, open a proposal, tap **Reject**, and
   tap into the reason textarea. This is the only dialog in the product that contains a text field, so
   it is the one case where a `TdDialog` is certainly rendered with the keyboard open. Confirm the
   dialog's footer (Cancel / Reject) stays reachable and that the body scrolls inside the dialog if
   the content is taller than the visual viewport.
5. **Keyboard-down regression.** Dismiss the keyboard while a dialog is still open. The sheet must
   snap back to the full-height mobile sheet with no leftover gap or offset at the bottom.
6. **Record the result** on `#1821`: browser/OS versions, `'visualViewport' in window`, pass/fail per
   step, and screenshots. Only then tick the `#1821` line in `OUTSTANDING_TASKS.md`. The two Nightly
   `mobile-safari` scenarios that have been red since 2026-08-24 (`#2180`) are emulated-geometry
   assertions — a real-phone pass does not turn that matrix green and does not close them.

## Teardown

```powershell
# 1. Stop Taskdeck with Ctrl+C in its window (packaged: wait for "Taskdeck stopped.").
# 2. Remove the firewall rules (admin PowerShell):
Remove-NetFirewallRule -DisplayName 'Taskdeck LAN test 5000'
Remove-NetFirewallRule -DisplayName 'Taskdeck LAN test 5173'   # only if created
# 3. Clear the process-scoped overrides (or just close those terminals):
Remove-Item Env:ASPNETCORE_URLS -ErrorAction SilentlyContinue
Remove-Item Env:ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue
Remove-Item Env:Cors__DevelopmentAllowedOrigins__0 -ErrorAction SilentlyContinue
Remove-Item Env:VITE_API_BASE_URL -ErrorAction SilentlyContinue
```

`$env:` assignments are process-scoped, so closing the terminals clears them; the explicit removals
are for a window you want to keep using. If you fell back to writing
`frontend/taskdeck-web/.env.local`, delete it — it is gitignored but would silently re-point later dev
runs. If you reclassified the network to Private, decide separately whether to leave it that way.
Restarted normally, Taskdeck returns to loopback-only.

## Caveats and limits

- **Use `http://0.0.0.0:<port>` literally in `ASPNETCORE_URLS` for the packaged app.** `http://+:5000`
  and `http://*:5000` are not parseable as absolute URIs, and a LAN-IP-only value such as
  `http://192.168.50.100:5000` contains no loopback or wildcard address — the packaged app's
  user-facing-URL resolver throws and the process exits with `TASKDECK_DESKTOP_FATAL`. If you must
  name a specific IP, add a loopback URL alongside it:
  `http://192.168.50.100:5000;http://127.0.0.1:5000`.
- **`ASPNETCORE_URLS` disables the packaged free-port fallback.** With it set, a busy port is a
  startup failure, not an automatic move to another port.
- **Do not put `urls` in `%LOCALAPPDATA%\Taskdeck\appsettings.local.json`.** The packaged listen URL
  is decided before that file is added to configuration, so the loopback default would already have
  been applied. Use the environment variable.
- **`http://<HOST_IP>` is not a secure context.** No service worker, no PWA install, no offline shell,
  no share target, no Web Share, no clipboard-write buttons (they are guarded and degrade). The one
  hard failure is the share-target capture queue, which calls `crypto.randomUUID()` unguarded — that
  route is reachable only from the installed PWA's share target, which cannot exist here. Ordinary
  capture through the Inbox API is unaffected.
- **Login works over plain HTTP.** The JWT lives in `localStorage` (ADR-0009) and travels as an
  `Authorization: Bearer` header — no cookies, so no `Secure`-cookie problem. There is no HTTPS
  redirect middleware anywhere in the API, and the HSTS header is only emitted on requests that are
  already HTTPS (and `EnableHsts` is `false` in Development anyway).
- **Path B CORS is the usual failure.** The key is `Cors:DevelopmentAllowedOrigins`, not
  `Cors:AllowedOrigins`; the origin must match the port Vite actually bound (5173 → 4173 → 5001); and
  the same policy covers the SignalR hub, so a wrong origin also kills realtime.
- **Path B bakes the host IP into the frontend at dev-server start.** If DHCP changes the host's
  address, restart Vite (and the API's CORS origin) with the new value.
- **Binding `0.0.0.0` exposes the whole API** — JWT-protected and rate-limited, but unencrypted — to
  every device on the subnet, and `AllowedHosts` is `*` so a LAN `Host` header is accepted. Keep the
  firewall rule subnet-scoped and remove it afterwards. This contradicts
  `WINDOWS_QUICK_START.md`'s loopback-only statement **only for the duration of the test**.
- **Path A tests the frontend that shipped in the ZIP.** `TdDialog.vue`, `useVisualViewport.ts` and
  `RejectProposalDialog.vue` are byte-identical between `v0.1.2` and `main` at `927236bd0`, so the
  `#1821` subject is the same. `CardModal.vue` is not: `main` added an inspector presentation that
  deliberately does not apply the visual-viewport style (it is not a fixed overlay). Do the `#1821`
  check in the modal presentation, or use Path B / the Path A variant if you need current `main`.
- **The standalone MCP HTTP host is a different thing** and is not covered here: it binds
  `127.0.0.1` by default with its own loopback host allowlist.
