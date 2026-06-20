# ADR-0041: Auto-Generate the Connector Encryption Key for the Desktop Exe (Headless Production Excluded)

- Status: Accepted
- Date: 2026-06-20
- Deciders: Repository maintainers
- Related: #1131 (CLI/fresh-machine bootstrap), ADR-0038 (Paper UI canonical / self-contained exe is the personal run path), the archive-pivot run-story

## Context

Taskdeck encrypts stored connector credentials with a symmetric key read from
`Connectors:EncryptionKey`. `Infrastructure/DependencyInjection.cs` throws at startup when that key is
empty, so the application cannot start without one.

`FirstRunBootstrapper.RunFirstRunChecks` already auto-generates secrets into `appsettings.local.json`
(next to the executable, via `AppContext.BaseDirectory`) so a checked-out or packaged build runs
without hand-edited config: the JWT secret is generated in **all** environments, but the connector
encryption key was generated only when **not** Production:

```csharp
if (!builder.Environment.IsProduction())
    EnsureConnectorEncryptionKey(...);
```

The original guard exists because a **headless cloud container** that auto-generated the key would get
an *ephemeral* one — a restart on an ephemeral or rebuilt filesystem would produce a new key and
silently lose the ability to decrypt previously-stored connector credentials. So Production was made to
require an operator-supplied, stable key, which `ValidateProductionSecrets` enforces by throwing.

The archive pivot (ADR-0038) makes the **self-contained desktop exe the canonical personal run path**,
and that exe runs with `ASPNETCORE_ENVIRONMENT=Production`. Under the old guard it therefore crashes on
first launch unless the user first exports `Connectors__EncryptionKey` — defeating the pivot's
"trivially easy to run / double-clickable" goal. But the desktop case does **not** share the cloud
container's hazard: a desktop install persists the generated key to `appsettings.local.json` on local
disk, so the key is **stable across restarts**.

The connector key's persistence is a security-relevant startup behavior, so per `CLAUDE.md` this
warrants an ADR.

## Decision

Auto-generate the connector encryption key when it is not configured **unless** the deployment is a
**headless Production** one. The decision is a pure, unit-tested policy:

```csharp
internal static bool ShouldAutoGenerateConnectorKey(bool isProduction, bool isHeadless)
    => !isProduction || !isHeadless;
```

- **Desktop exe (Production, NOT headless):** generate a 256-bit key and persist it to
  `appsettings.local.json` (created `0600` on Unix before the secret is written; reloaded on the next
  launch via `AddLocalConfigFile`). The exe becomes runnable with no manual configuration.
- **Headless Production (CI / cloud container):** detected via `CI` / `TF_BUILD` / `GITHUB_ACTIONS` /
  `TASKDECK_HEADLESS`. The key is **not** generated; the deployment must supply a stable key, and
  `ValidateProductionSecrets` hard-fails (throws) if it is missing — unchanged behavior.
- **Non-Production (Development/Staging/Test):** generate as before — unchanged behavior.

`RunFirstRunChecks` runs before `ValidateProductionSecrets` in `Program.cs`, so a desktop launch
generates the key first and then passes validation.

## Alternatives Considered

- **Keep requiring an operator-supplied key in all Production.** Rejected: it is the status quo that
  makes the canonical desktop exe non-double-clickable, directly blocking the pivot run-story.
- **Auto-generate in all Production (including cloud containers).** Rejected: reintroduces the original
  hazard — an ephemeral key on a container restart loses the ability to decrypt stored connector
  credentials. The headless carve-out preserves that protection where it matters.
- **Generate the key into an OS keychain / external secret store.** Rejected as over-engineered for a
  single-user local tool; the existing `appsettings.local.json` mechanism (already used for the JWT
  secret) is consistent and sufficient.
- **Gate on a bespoke `FirstRun:AutoConnectorKey` flag instead of headless detection.** Rejected: adds
  configuration surface; the existing `IsHeadlessEnvironment()` signal already distinguishes desktop
  from CI/cloud and is reused here.

## Consequences

- The self-contained desktop exe runs on first launch with no manual key configuration (pivot goal:
  trivially easy to run).
- The generated connector key lives in `appsettings.local.json` next to the exe. **Operationally:** that
  file must be backed up alongside the SQLite database — deleting it makes previously-stored connector
  credentials undecryptable (the encrypted data remains but cannot be read). This is the same trade-off
  that already applies to the auto-generated JWT secret.
- Cloud/CI behavior is unchanged: headless Production still requires a supplied stable key and hard-fails
  without one.
- A desktop deployment intentionally run headless (operator sets `TASKDECK_HEADLESS`) opts into the
  supply-your-own-key contract — an explicit, documented trade-off.
- `ShouldAutoGenerateConnectorKey` is covered by a unit test over all four `(isProduction, isHeadless)`
  combinations, decoupling the policy from the process's own environment (the CI runner is itself
  "headless", which made the branch otherwise hard to test directly).

## References

- #1131 — CLI/fresh-machine bootstrap (this ADR addresses the API/desktop-exe connector-key half)
- ADR-0038 — Paper UI Is the Canonical Frontend / self-contained exe is the personal run path
- `backend/src/Taskdeck.Api/FirstRun/FirstRunBootstrapper.cs` — `RunFirstRunChecks`,
  `ShouldAutoGenerateConnectorKey`, `EnsureConnectorEncryptionKey`, `ValidateProductionSecrets`
- `backend/tests/Taskdeck.Api.Tests/FirstRun/FirstRunBootstrapperTests.cs` — policy unit test
