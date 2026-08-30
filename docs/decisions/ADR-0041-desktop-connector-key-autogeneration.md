# ADR-0041: Auto-Generate the Connector Encryption Key for the Desktop Exe (Headless Production Excluded)

- Status: Accepted
- Date: 2026-06-20
- Deciders: Repository maintainers
- Related: #1131 (CLI/fresh-machine bootstrap), #1241 (owner-only secret files), #1242 (durable desktop config), ADR-0038 (Paper UI canonical / self-contained exe is the personal run path), the archive-pivot run-story

## Amendment (2026-08-20, #1242)

Non-headless Production now stores `appsettings.local.json` beside the default
SQLite database in the durable per-user Taskdeck app-data directory, not beside
the executable. Web, MCP HTTP, and MCP stdio resolve one exact path using the
same environment/headless policy. Development and headless Production retain
the executable-local compatibility path.

A valid v0.1 executable-local file is imported whole only when the durable file
is absent; the source is retained, the durable file wins on conflict, and both
files are owner-only. The import is locked, atomic, and non-overwriting. An
existing database without a supplied or recoverable connector key fails before
either connector or JWT identity is generated. A missing durable database plus
an executable-local v0.1 database also fails closed rather than silently creating
a blank replacement. This amendment implements durable identity selection while
preserving the original headless-Production decision. The post-ZIP harness now
proves launch from an unrelated working directory and durable identity reuse across
two extraction directories; Explorer, shortcut, and clean-Windows behavior remains
tracked by #1242 and #1876.

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
  the durable per-user `appsettings.local.json`, reloaded on the next launch via `AddLocalConfigFile`.
  The exe becomes runnable with no manual configuration. The file is created owner-only (`0600` on Unix;
  protected current-user DACL on Windows) before the secret is written. If the write fails, startup
  **fails loudly** (throws) rather than running with
  an ephemeral in-memory key that would be lost on restart and orphan stored connector credentials.
- **Headless Production (generic CI / cloud container):** an unmarked server process under `CI` /
  `TF_BUILD` / `GITHUB_ACTIONS`, or any process with `TASKDECK_HEADLESS`, does not generate the key;
  the deployment must supply a stable value and `ValidateProductionSecrets` hard-fails if it is
  missing. **The container image (`deploy/docker/backend.Dockerfile`) sets
  `TASKDECK_HEADLESS=true`**, so bare `docker run`, Compose, and Terraform retain that fail-closed
  posture. A marked desktop package is the deliberate exception: ambient CI suppresses automatic
  browser launch but does not suppress its durable bootstrap, allowing the hosted post-ZIP gate to
  exercise the same persisted-identity path as a user launch without opening a browser on the runner.
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
  configuration surface; the package marker plus existing CI / explicit-headless signals distinguish
  durable desktop bootstrap from generic CI/cloud and are reused here.

## Consequences

- The self-contained desktop exe runs on first launch with no manual key configuration (pivot goal:
  trivially easy to run).
- The generated connector key lives in the durable per-user `appsettings.local.json`. **Operationally:** that
  file must be backed up alongside the SQLite database — deleting it makes previously-stored connector
  credentials undecryptable (the encrypted data remains but cannot be read). The runtime log emitted at
  generation states this. The JWT secret shares the same persistence/backup model, **but not the headless
  behavior:** `EnsureJwtSecret` runs unconditionally (it auto-generates even in headless Production, where
  an ephemeral JWT secret only forces re-login), whereas the connector key is deliberately *not*
  auto-generated in headless Production because an ephemeral one would cause data loss, not just re-login.
- **At rest, the key file is owner-only on both Unix and Windows.** A filesystem that cannot enforce the
  required mode/DACL fails closed instead of receiving plaintext secrets.
- **The key and default database now share the durable per-user Taskdeck directory.** Moving or upgrading
  the executable does not rotate identity. A complete v0.1 executable-local config is imported once when
  the durable target is absent and retained for recovery; an existing durable file is authoritative.
- **An empty higher-priority `Connectors__EncryptionKey` reuses the persisted key — it never overwrites or
  churns it.** Because `appsettings.local.json` loads *before* environment variables (so env vars win), an
  env var set to an empty/whitespace value (e.g. a copied service/env template) would mask the persisted key.
  Critically, the generate path must not regenerate in that case. Before generating, the bootstrapper reads
  the key directly from the file
  (`TryReadPersistedConnectorKey`, bypassing source precedence) and, when one exists, **reuses it** for this
  process — so the original key (and the credentials it protects) is never destroyed and the app keeps
  working across restarts despite the misconfigured variable. A warning names the remediation (unset the
  empty variable). The final read-or-create is also serialized under the path lock, so concurrent fresh
  launches reuse one persisted winner. `TryReadPersistedConnectorKey` is unit-tested over
  present/missing/corrupt/empty content.
- **A corrupt `appsettings.local.json` is preserved, not silently discarded.**
  If the durable Production file exists but is unparsable (an interrupted write or a hand-edit), startup
  fails closed with the original left in place because it may hold the only recoverable connector key.
  Development and compatibility hosts retain `QuarantineCorruptLocalConfig`: it copies malformed input to
  an owner-only timestamped `.corrupt-*` sibling before removing the original. If a database already exists
  and no connector key remains recoverable, bootstrap stops before generating either secret. A recovery
  copy that cannot be created securely also stops startup. The same preservation guards an in-process
  overwrite.
- **Staging (and any non-Production environment) auto-generates the key and is never validated**
  (`ValidateProductionSecrets` early-returns for non-Production). So a cloud Staging container does **not**
  behave like Production — it relies on its local `appsettings.local.json` persisting, with the same
  backup caveat. This is unchanged by this ADR but worth stating explicitly.
- Cloud/container behavior remains fail-closed through `TASKDECK_HEADLESS`, and unmarked Production
  under CI remains bootstrap-headless. Marked desktop CI instead performs durable bootstrap while
  keeping browser launch suppressed.
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
- `backend/tests/Taskdeck.Api.Tests/FirstRun/LocalConfigPathMigrationTests.cs` — durable-path, import,
  concurrency, permission, and database-identity regression tests
