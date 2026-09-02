# CLI hardening — actor identity and claims-first authorization parity (#1131)

Last Updated: 2026-09-02

> Curated from the v0.3/v0.4 acceleration bundle (grounded `221aa88c8`, 2026-08-30) and validated against `main` `de488fea0` on 2026-09-02 under tracker #2376 (follow-up to #2348). Planning input, not authority: the live issue, the accepted ADRs and `docs/STATUS.md` win. Corrections to the bundle are listed in the last section.

## Outcome

A CLI board mutation is either authorized exactly as the equivalent HTTP call is, or the CLI is declared in writing as a local-admin surface with a stated trust boundary. The one outcome that is not acceptable is the current undeclared bypass, which reads as an oversight in both directions.

## Live dependencies (verified 2026-09-02)

| Dependency | State | Note |
| --- | --- | --- |
| AC1 fresh-machine bootstrap | **shipped** | `backend/src/Taskdeck.Cli/CliFirstRunBootstrapper.EnsureConnectorEncryptionKey` runs before `AddInfrastructure` in `Program.cs`; PR #1177 / `54fb4770b` |
| Maintainer choice (a) local-admin vs (b) route authorization | **not recorded** | The 2026-08-23 comment frames it explicitly and deliberately does not decide. It is the only blocker. **Never inferred** |
| `Taskdeck.Cli.Tests` | exists | The test project the residual lands in |
| #1130 SQLite WAL / busy-timeout work | shipped | Makes the body's third evidence bullet (`Database.Migrate()` per invocation feeding a multi-process race) stale — see corrections |

## Child slices (one PR each, in order)

| Id | Outcome | Depends on | Startable before predecessors merge? |
| --- | --- | --- | --- |
| `CLI-0-decision` | The maintainer records (a) or (b) on the issue; the body is reduced to AC2 alone | — | **No — human-only** |
| `CLI-1a-trust-boundary` *(if (a))* | A `docs/CLI.md` trust-boundary statement plus README/AGENTS lines: the CLI is an unrestricted local-admin tool, must never be exposed on a shared host or run as a service account, and filesystem access to the SQLite file is already total access. An architecture test pins that no CLI handler claims to authorize | CLI-0 | No |
| `CLI-1b-actor-parity` *(if (b))* | Boards / Cards / Columns handlers resolve the CLI actor and pass through the same write bar the controllers use, with a stable non-zero exit on denial | CLI-0 | No |
| `CLI-2-body-cleanup` | Strike or re-verify the stale `Database.Migrate()` evidence bullet; mark AC1 delivered with its PR | — | **Yes.** Pure issue hygiene, and #2235 asks for exactly this class of cleanup |
| `CLI-3-ops-surface` *(optional, only under (b))* | Group the pre-host operator commands under one documented surface and one published exit-code table | CLI-1b | Partly — see the correction on the bundle's `ops` group |

## Architecture

| Concern | Type | Status | Note |
| --- | --- | --- | --- |
| CLI system actor | `Taskdeck.Cli/Commands/CliActorIdentity` (`taskdeck_cli_actor`, `cli-actor@system.taskdeck`, non-routable so it cannot be registration-hijacked) | **exists** | The concept is built |
| Actor resolution | `GetOrCreateCliActorIdAsync` in `ApiKeysCommandHandler` (line 185) and `BoardsCommandHandler` (line 162) | **exists, partial** | Two of five handlers resolve an actor. `CardsCommandHandler` and `ColumnsCommandHandler` resolve none |
| The bypass | `CardsCommandHandler.AddAsync` calls `_cardService.CreateCardAsync(createRequest)` — the actor-less overload at `CardService.cs:35` | **exists (the defect)** | Reaches `CreateCardAsync(dto, cardId: null, actorUserId: null, …)`. Any board id supplied on the command line is operated on regardless of ownership |
| Actor-carrying overload | `CardService.CreateCardAsync(CreateCardDto, Guid? cardId, Guid? actorUserId, …)` at `CardService.cs:53` | **exists — but attributes only** | Its own doc comment says the actor is for the audit row. `CardService` contains **no** `BoardAccess` reference; grepping `BoardAccess` in that file matches nothing. Passing an actor does *not* authorize |
| The real write bar | `AuthorizationService.CanWriteBoardAsync` (API side, #1794/#1827), mirrored for the proposal lanes by `BoardAccessBar.Write` in `AutomationPolicyEngine.ValidateBoardAccessAsync` | **exists** | This — not the actor overload — is what option (b) has to reach from the CLI |
| Pre-host operator commands | `DatabaseRecoveryCommand` (`--backup` / `--restore`), `ConnectorVerificationCommand` (`--verify-connectors`), `VersionCommand` (`--version`) | **exists** | Intercepted in `Program.cs` **before** the host is built, deliberately: they must not migrate, provision keys, or start providers |
| Exit codes | `Taskdeck.Cli/Commands/ExitCodes` — `Success = 0`, `Failure = 1`, `Usage = 2` | **exists** | Published as the operator contract in `docs/ops/DISASTER_RECOVERY_RUNBOOK.md` |
| Machine-readable output | `--json` flag on the domain handlers; `ConsoleOutput.WriteJson`; logging providers cleared in `Program.cs` so stdout stays clean JSON | **exists** | The "content-free machine-readable output" the bundle asks for is largely already the shipped shape |
| An `ops` command group | — | **absent, and probably should stay absent** | See corrections |

## Implementation plan

**Preflight.** Read the single comment in full. AC1 is delivered; AC2 is a product decision. Do not pick a side, and do not open a PR that quietly implements (b) — that is the failure mode the comment exists to prevent.

**Under (a).** The cost is a dated trust-boundary note plus README/`AGENTS.md` lines, and an honest statement that the CLI is not a multi-user surface. Add one architecture test that fails if a CLI handler starts *claiming* an authorization check it does not perform, so the declaration cannot silently rot.

**Under (b).** The work is larger than the 2026-08-23 comment implies: the actor-carrying overloads attribute but do not authorize, so a board-access check has to be reached from three handlers, and the CLI's own actor has to be a *member* of the boards it writes to — otherwise every existing CLI script breaks on the first run after the change. Plan the migration story for the CLI actor's board memberships before writing code.

**Exit-code discipline either way.** The published contract is `0 / 1 / 2`. A denial under (b) must map onto it (`1`), or the runbook table changes in the same PR. Do not introduce a second exit-code vocabulary.

## Test plan

- [ ] Fresh machine: `taskdeck boards list` against a clean environment with no `TASKDECK_CONNECTORS__ENCRYPTIONKEY` exported — `dotnet test backend/tests/Taskdeck.Cli.Tests/Taskdeck.Cli.Tests.csproj -c Release -m:1`
- [ ] `--version` still answers on a machine whose data directory is missing or corrupt (regression pin for the pre-host interception)
- [ ] *(b)* A card add against a board the CLI actor cannot write to fails with exit `1` and a stable error code, and creates no card and no audit row
- [ ] *(b)* A successful CLI card add writes an audit row attributed to the CLI actor, not to `null`
- [ ] *(a)* An architecture test asserting no CLI handler references an authorization type, so the trust-boundary note stays true
- [ ] Operator commands mutate no application state: `--verify-connectors` leaves the database byte-identical
- [ ] Exit-code matrix: success, usage error, and failure for each command group, asserted against the runbook table
- [ ] Redaction: no key, ciphertext, plaintext or exception detail appears on stdout or stderr for any operator command failure
- [ ] Docs: `node scripts/check-docs-governance.mjs`

## Edge cases

- The CLI runs on the same machine as the API while a migration is pending — `SerializedMigrator` takes the file lock and a fail-closed pre-migration snapshot; a CLI invocation must not be the thing that migrates unprotected.
- Two CLI invocations racing on one SQLite file.
- *(b)* The CLI actor exists but has no board memberships — every existing script fails on the upgrade unless the migration grants them.
- *(b)* A board is deleted between the access check and the write.
- A command run in a non-interactive automation context where stderr is discarded — the exit code is the only signal.
- `--json` output containing an error message that quotes a file path or a connection string.
- `ContextFabricBootstrap.RunCaptureBackfill` runs on every CLI invocation after migration; a hardened CLI must not make that a per-command cost on a large database.

## Reference material

| Kind | Archived path | Good for | Caveats |
| --- | --- | --- | --- |
| C# candidate | `docs/analysis/2026-08-30-acceleration-bundle/candidates/dotnet/OpsCommandExitCodes.cs` | The idea that automation branches on codes, not prose | **Conflicts with shipped reality**: it defines eight codes (10/20/30/31/40/70) against the shipped three, and `docs/ops/DISASTER_RECOVERY_RUNBOOK.md` publishes the 0/1/2 table as the operator contract. Adopting it is a breaking change to a documented interface |
| Ops contract | `.../candidates/ops/CONNECTOR_KEY_VERIFICATION_CONTRACT.md` | The content-free-output discipline and the "NoCredentials is not proof" rule | Its proposed command shape (`taskdeck ops verify-connector-key [--json]`) is not what shipped |
| Audit note | `.../audit-m4/HIGH_LEVERAGE_RESIDUALS.md` §"CLI residual: #1131" | The invocation → actor → policy → command framing | Written as though the decision were made; it is not |

## Corrections to the bundle

1. **Bundle pack:** "The issue body is stale: connector-key bootstrap and serialized pre-migration backup are already in Program.cs." **True** — and the live issue already records it (AC1 delivered, PR #1177). **Consequence:** the pack repeats a correction the issue made on 2026-08-23; the residual is smaller than the pack implies.
2. **Bundle pack:** "route domain commands through the same authorization policy as API paths" is listed as a residual to implement. **True:** the live issue treats (a) *declare it a local-admin tool* and (b) *route authorization* as an unresolved product decision. **Consequence:** the pack silently picks (b). An agent following it would implement one side of an open maintainer decision — the exact outcome the issue's realignment forbids.
3. **Bundle pack / `TRACKER_DRIFT.md`:** implies the actor-carrying `CreateCardAsync` overloads are enough to route authorization. **True on `main`:** `CardService` performs board and column existence checks and archived-board rejection, but contains no `BoardAccess` lookup at all; the actor parameter feeds the audit row. The real bar is `AuthorizationService.CanWriteBoardAsync`. **Consequence:** option (b) is materially more work than "use the other overload".
4. **Bundle pack:** "the dispatcher has no ops command surface" and "Add a separate `ops` command group". **True:** the operator commands **exist** — `--backup`, `--restore`, `--verify-connectors` — and are deliberately intercepted in `Program.cs` *before* `Host.CreateApplicationBuilder`, with comments citing #2238/#2239 explaining why. Routing them through `CommandDispatcher` would build the host, migrate the database and provision a key, which is precisely what a recovery command must not do. **Consequence:** the residual is *documenting* the operator surface, not relocating it. A naive `ops` group would be a regression.
5. **Bundle pack:** "Define machine-readable exit codes and content-free output for automation." **True:** both exist — `ExitCodes` 0/1/2 published in the runbook, `--json` output, cleared logging providers, and stable error codes such as `CONNECTOR_KEY_MISSING` / `CONNECTOR_KEY_INVALID` / `CONNECTOR_DATABASE_UNAVAILABLE`. **Consequence:** the gap is a published table for the *domain* commands, not a new vocabulary.
6. **Live issue body, third evidence bullet:** "`Cli/Program.cs:43` runs `Database.Migrate()` per invocation (feeds the multi-process race)." **True on `main`:** `Program.cs` calls `SerializedMigrator.Migrate(dbContext, backupSettings)`, which serializes migrations across API/MCP/CLI processes via a file lock (#1164) and takes a fail-closed pre-migration snapshot (#1803). **Consequence:** the bullet is stale and should be struck when the body is next edited — the 2026-08-23 comment flagged it and it is still there.
