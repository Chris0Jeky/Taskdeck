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
  `appsettings.local.json`, reloaded on the next launch via `AddLocalConfigFile`. The exe becomes
  runnable with no manual configuration. On Unix the file is created `0600` before the secret is written;
  **on Windows (the primary desktop target) the file is NOT permission-restricted** — at-rest protection
  relies on the user-profile / disk security. Hardening the Windows ACL is tracked in #1241. If the write
  fails (e.g. a read-only install directory), startup **fails loudly** (throws) rather than running with
  an ephemeral in-memory key that would be lost on restart and orphan stored connector credentials.
- **Headless Production (CI / cloud container):** detected via `CI` / `TF_BUILD` / `GITHUB_ACTIONS` /
  `TASKDECK_HEADLESS`. The key is **not** generated; the deployment must supply a stable key, and
  `ValidateProductionSecrets` hard-fails (throws) if it is missing — unchanged behavior. Because the
  CI-marker set does not by itself identify a server/container, **the container image
  (`deploy/docker/backend.Dockerfile`) sets `TASKDECK_HEADLESS=true`**, so every container — bare
  `docker run`, compose, or terraform — is classified headless and cannot auto-generate an ephemeral key.
  A desktop machine with an ambient `CI` variable is likewise treated as headless and will hard-fail
  (fail-safe, not silent) until a key is supplied or `CI` is unset.
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
  credentials undecryptable (the encrypted data remains but cannot be read). The runtime log emitted at
  generation states this. The JWT secret shares the same persistence/backup model, **but not the headless
  behavior:** `EnsureJwtSecret` runs unconditionally (it auto-generates even in headless Production, where
  an ephemeral JWT secret only forces re-login), whereas the connector key is deliberately *not*
  auto-generated in headless Production because an ephemeral one would cause data loss, not just re-login.
- **At rest, the key file is only `0600`-restricted on Unix; on Windows it inherits default NTFS ACLs**
  (tracked in #1241). For the single-user desktop target this relies on the user's profile security.
- **The key persists next to the exe (`AppContext.BaseDirectory`), not in the app-data directory where
  the database lives.** Moving or upgrading the exe to a *different folder* therefore orphans the key
  while the database (in `%LOCALAPPDATA%/Taskdeck`) is reused — the new exe regenerates a different key and
  cannot decrypt the reused credentials. Until this is addressed (relocate persisted secrets to the durable
  app-data location, tracked in #1242), back up `appsettings.local.json` and prefer in-place upgrades.
- **An empty higher-priority `Connectors__EncryptionKey` fails closed — it does not silently churn keys.**
  Because `appsettings.local.json` loads *before* environment variables (so env vars win), an env var set to
  an empty/whitespace value (e.g. a copied service/env template) would mask the persisted key. The desktop
  generate path verifies the persisted key is the effective value after reload and, when it is not, **throws**
  in Production rather than running on a process-local in-memory key that the next launch would regenerate
  *differently* — which would orphan connector credentials saved this run. The remediation (unset the empty
  variable or set a real key) is named in the exception. `ResolveConnectorKeyPersistOutcome` is unit-tested
  over the visible / masked-in-Production / masked-in-harness cases.
- **A corrupt `appsettings.local.json` is preserved, not silently overwritten.** If the file exists but is
  unparsable (an interrupted write or a hand-edit), it may still hold the only recoverable copy of the
  connector key. `PersistValue` now copies it to a timestamped `.corrupt-*` sibling before rewriting, so an
  operator can recover the old key rather than have it replaced by a freshly generated one (which would
  orphan credentials in the reused database).
- **Staging (and any non-Production environment) auto-generates the key and is never validated**
  (`ValidateProductionSecrets` early-returns for non-Production). So a cloud Staging container does **not**
  behave like Production — it relies on its local `appsettings.local.json` persisting, with the same
  backup caveat. This is unchanged by this ADR but worth stating explicitly.
- Cloud/CI behavior is unchanged: headless Production still requires a supplied stable key and hard-fails
  without one.
- **The bundled AWS single-node Terraform module supplies that stable key itself.** `user_data.sh.tftpl`
  generates the connector key once (`openssl rand -base64 32`) and persists it to
  `/var/lib/taskdeck/connector-encryption.key` on the durable EBS data volume — the same volume that holds
  the SQLite database — then injects it into the container via `.env`. The key therefore survives instance
  replacement (with `user_data_replace_on_change`) alongside the credentials it protects: one failure
  domain, so the volume can never be retained with an undecryptable database, nor the key orphaned from it.
  Unlike the JWT secret (sourced from SSM/KMS for central rotation), the connector key's natural lifecycle
  is tied to the data volume, so co-locating it there is both simpler and avoids the empty-value crash that
  an unprovisioned `Connectors__EncryptionKey` would otherwise cause once the image is headless.
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
