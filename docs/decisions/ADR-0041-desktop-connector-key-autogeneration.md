# ADR-0041: Auto-Generate the Connector Encryption Key for the Desktop Exe (Headless Production Excluded)

- Status: Accepted
- Date: 2026-06-20
- Deciders: Repository maintainers
- Related: #1131 (CLI/fresh-machine bootstrap), #1241 (owner-only local config), #1242 (durable desktop local config), ADR-0038 (Paper UI canonical / self-contained exe is the personal run path), the archive-pivot run-story

## Implementation Update (2026-07-27)

Issue #1242 closes the relocation limitation recorded by this ADR. Packaged desktop Production now
stores `appsettings.local.json` in the same durable OS app-data directory as its resolved SQLite
database, while Development, staging/test, and headless Production retain the historical
executable-local path. A first launch after upgrade imports the complete executable-local legacy JSON
exactly once when the durable target is absent; an existing durable target always wins. Both copies
are owner-only, and import/permission/race ambiguity fails closed. API and CLI also refuse to generate
replacement secrets when an existing database has no recoverable connector key. The headless
Production supplied-key decision itself is unchanged.

## Context

Taskdeck encrypts stored connector credentials with a symmetric key read from
`Connectors:EncryptionKey`. `Infrastructure/DependencyInjection.cs` throws at startup when that key is
empty, so the application cannot start without one.

At the time of this decision, `FirstRunBootstrapper.RunFirstRunChecks` auto-generated secrets into
an executable-local `appsettings.local.json` so a checked-out or packaged build ran without
hand-edited config: the JWT secret was generated in **all** environments, but the connector
encryption key was generated only when **not** Production. The implementation update above now gives
packaged desktop Production a durable app-data path without changing that historical decision context:

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

- **Desktop exe (Production, NOT headless):** generate a 256-bit key and persist it to the durable
  OS app-data `appsettings.local.json`, reloaded on the next launch via `AddLocalConfigFile`. The exe
  becomes runnable with no manual configuration and survives an executable-folder move. The file is
  created owner-only (`0600` on Unix; protected current-user DACL on Windows) before secrets are
  written. An executable-local legacy file is imported atomically only when the durable target is
  absent, then retained as an owner-only recovery copy. If durable persistence or migration cannot be
  proved safe, startup **fails loudly** rather than running with ephemeral replacement material.
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
- The generated connector key and JWT secret live in the packaged desktop's durable app-data
  `appsettings.local.json`, beside the resolved SQLite database rather than beside the executable.
  **Operationally, both files remain one recovery set:** deleting the config makes stored connector
  credentials undecryptable, while losing only the JWT secret forces re-login. The connector key is
  deliberately *not* auto-generated in headless Production because an ephemeral one would cause data
  loss, not just re-login.
- **The durable and retained legacy local-config files are owner-only before their secrets are read or
  written.** Unix uses mode `0600`; Windows uses a protected current-user DACL. Migration uses
  cross-process locks and an atomic non-overwriting move, never merges two secret files, and treats the
  durable target as authoritative once it exists.
- **Moving or upgrading the desktop executable no longer orphans its key.** A first run with no durable
  target imports the complete executable-local legacy JSON and retains the secured source as recovery
  evidence. If an existing database has no supplied or recoverable persisted connector key, startup
  refuses to generate either replacement secret; recovery must be explicit.
- **An empty higher-priority `Connectors__EncryptionKey` reuses the persisted key — it never overwrites or
  churns it.** Because `appsettings.local.json` loads *before* environment variables (so env vars win), an
  env var set to an empty/whitespace value (e.g. a copied service/env template) would mask the persisted key.
  Critically, the generate path must not regenerate in that case: `PersistValue` overwrites the file in
  place, so a new key would *destroy* the masked one before any after-the-fact check could fire. Instead,
  before generating, the bootstrapper reads the key directly from the file
  (`TryReadPersistedConnectorKey`, bypassing source precedence) and, when one exists, **reuses it** for this
  process — so the original key (and the credentials it protects) is never destroyed and the app keeps
  working across restarts despite the misconfigured variable. A warning names the remediation (unset the
  empty variable). `TryReadPersistedConnectorKey` is unit-tested over present/missing/corrupt/empty content.
- **A corrupt `appsettings.local.json` self-heals; the corrupt file is preserved, not silently discarded.**
  If the file exists but is unparsable (an interrupted write or a hand-edit) it may still hold the only
  recoverable copy of the connector key — *and* a malformed optional JSON source throws at config-build time,
  which would crash startup on every launch. `AddLocalConfigFile` therefore quarantines it first
  (`QuarantineCorruptLocalConfig`): it copies the file to a timestamped `.corrupt-*` sibling for operator
  recovery, then removes the original so the optional source loads as "missing" and the app starts fresh
  (regenerating a key) rather than failing to launch. The same preservation also guards an in-process
  overwrite in `PersistValue`.
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
- #1242 — durable desktop local-config relocation, legacy import, and existing-database key guard
- ADR-0038 — Paper UI Is the Canonical Frontend / self-contained exe is the personal run path
- `backend/src/Taskdeck.Api/FirstRun/FirstRunBootstrapper.cs` — `RunFirstRunChecks`,
  `ResolveLocalConfigPath`, `PrepareLocalConfigFile`, `EnsureBootstrapSecrets`,
  `ShouldAutoGenerateConnectorKey`, `EnsureConnectorEncryptionKey`, `ValidateProductionSecrets`
- `backend/src/Taskdeck.Application/Bootstrap/BootstrapFileLock.cs` and
  `BootstrapFileSecurity.cs` — shared API/CLI cross-process and owner-only persistence contract
- `backend/tests/Taskdeck.Api.Tests/FirstRun/FirstRunBootstrapperTests.cs` and
  `LocalConfigPathMigrationTests.cs` — policy, migration, race, ACL, and data-loss regressions
