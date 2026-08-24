# v0.1.1 Windows Startup Incident And Recovery Checkpoint

- Status: implementation resolved (`#1876`, PR `#2016`, merge `909e33f9`); v0.1.2 remains unreleased
- Recorded: 2026-08-22
- Resolved on main: 2026-08-23
- Public release: [`v0.1.1`](https://github.com/Chris0Jeky/Taskdeck/releases/tag/v0.1.1)
- Incident evidence: [`#1876` comment](https://github.com/Chris0Jeky/Taskdeck/issues/1876#issuecomment-5380396057)
- Current release checkpoint: [`.codex/memories/00_ACTIVE.md`](../../.codex/memories/00_ACTIVE.md)

## Executive summary

The public v0.1.1 ZIP is not corrupt, but the ordinary double-click experience is not reliable on
an upgrading Windows profile. On the maintainer's profile, both Downloads and Desktop extractions
failed before opening a listener because the process inherited a user-scoped retired Taskdeck
Gemini selector and retired `Llm__Gemini__*` settings. The packaged top-level catch discarded the
existing fixed migration guidance and showed only a generic port/data-folder error.

The earlier public-artifact acceptance used an intentionally hermetic child environment that
removed all inherited `Llm__*` names. That remains valid evidence for clean install, persistence,
review-first OpenAI behavior, and archive integrity, but it did not test the ordinary inherited
upgrade environment and must not be generalized to that path. Issue `#1876` was reopened for the
correction and is now closed as completed by PR `#2016`.

At incident time, a temporary compatibility launch of the unchanged public executable worked after
the persistent non-secret user provider selector was changed explicitly from Gemini to OpenAI; all
credential-bearing Gemini and OpenAI variables remained present and were not read, copied, printed,
or deleted. The permanent correction is now merged on main at `909e33f9`, after full backend tests,
review, and exact-head hosted CI. It is not a public v0.1.2 artifact: no v0.1.2 tag or release exists,
and the live milestone, final exact-main candidate evidence, and maintainer release-deck acceptance
still gate any tag.

## What the user saw

Normal launch from:

`%USERPROFILE%\Desktop\taskdeck-v0.1.1-win-x64\Taskdeck.Api.exe`

printed:

```text
TASKDECK_DESKTOP_STARTING
TASKDECK_DESKTOP_DATA
TASKDECK_DESKTOP_BOOTSTRAP jwt_created=false connector_created=false
TASKDECK_DESKTOP_FATAL code=startup_failed
Taskdeck could not start. Check that the configured port is available and the data folder is writable.
```

No Taskdeck listener existed on port 5000. Extracting the same public ZIP under Downloads and
Desktop produced the same user-visible result because both processes inherited the same user
environment and used the same packaged startup policy.

## Verified cause

The causal chain is direct:

1. `Llm__Provider=Gemini` and four `Llm__Gemini__*` names were present at user scope. Only names,
   scope, presence, and the non-secret provider selector were inspected; values stayed redacted.
2. `LlmProviderRegistration` rejects the retired Gemini selector/settings before the host is built.
3. `Program.cs` catches every packaged startup exception but does not bind the exception.
4. `DesktopRuntime.WriteFatalStartup()` therefore emits only `code=startup_failed` plus a generic
   port/data-folder sentence, even though the provider layer already has static migration guidance.
5. `scripts/ci/windows_desktop_archive.py` deliberately removes inherited `LLM__*` names for the
   hermetic release journey. That protects CI from operator credentials, but means the suite did
   not exercise the ordinary Explorer inherited-environment path.

This is not a ZIP-byte, extraction-directory, database, port, browser, or AppData-writability root
cause.

## Attempts and results

| Attempt | Result | Conclusion |
| --- | --- | --- |
| Launch the Desktop extraction normally | Failed with generic `startup_failed`; no listener | Reproduced the user's screenshot |
| Confirm port 5000 and Taskdeck process state before launch | No prior listener or Taskdeck process | Ruled out an initial port collision |
| Launch the exact public executable with the existing AppData profile | Same failure | Not a shortcut or working-directory defect |
| Launch with a new isolated `LOCALAPPDATA` | Bootstrap created new identity, then the same failure | Ruled out stale database/data-folder state |
| Remove only retired Gemini child settings while retaining `Provider=Gemini` | Still failed | The selector itself was also retired |
| Child-only `Provider=OpenAI` plus omitted retired Gemini child settings | Reached `TASKDECK_DESKTOP_READY` on loopback | Proved the configuration cause without changing credential values |
| Launch the unchanged public Desktop executable with that child-only compatibility environment against normal AppData | Started and served `/health/ready` and `/` with HTTP 200 | Immediate working workaround |
| Persist user-scoped `Llm__Provider=OpenAI` and broadcast the Windows environment change | Verified user selector is OpenAI; all provider variable names remain present | Non-secret, reversible migration; existing shells may retain their old process environment |

The compatibility instance was last verified as PID `10596`, executable path exactly under the
Desktop extraction, listening only on `127.0.0.1:5000`, with `/health/ready` and `/` returning 200.
PID and listener state are transient and must be rechecked rather than trusted as a later fact.

## Incident-time user-level configuration boundary

- User-scoped `Llm__Provider` was changed to `OpenAI` during the incident response.
- Four user-scoped `Llm__OpenAi__*` names were present.
- Four user-scoped `Llm__Gemini__*` names were present.
- No value was printed or retained in evidence.
- v0.1.1 still rejects the remaining retired Gemini section even after a supported provider is
  selected. The merged correction treats those keys as inert only after an explicit
  supported selector; it would still fail closed for `Provider=Gemini` and the retired Compose
  presence marker.

These are dated observations, not a current machine-state assertion. Recheck names and process state
without reading values before relying on them.

### Safe v0.1.1 compatibility launch

The persistent selector change alone does not make ordinary v0.1.1 double-click launch safe while
the retired child names remain. Launch it from a fresh PowerShell process that omits those names
only from its own environment:

```powershell
$env:Llm__Provider = 'OpenAI'
Remove-Item Env:Llm__Gemini__* -ErrorAction SilentlyContinue
$taskdeckV011 = Join-Path $env:USERPROFILE 'Desktop\taskdeck-v0.1.1-win-x64\Taskdeck.Api.exe'
& $taskdeckV011
```

This does not change or reveal persistent user values. Close that PowerShell window to discard the
temporary environment. Do not delete persistent credential-bearing variables as a workaround.

## Delivered permanent correction

Preserved worktree:

`<Taskdeck repo>\.worktrees\codex-1876-desktop-retired-provider-diagnostics`

Branch and delivered head:

- branch `issue-1876/desktop-retired-provider-diagnostics-v2`
- exact PR head `71f30964a517dfe4da1459e09823d0e786b40376`
- merged by PR `#2016` as `909e33f99fb191d33d18854ef3b5195b7afd653a`

The delivered correction adds:

- a typed retired-provider configuration exception/reason;
- stable packaged fatal marker
  `TASKDECK_DESKTOP_FATAL code=retired_provider_configuration`;
- static migration instructions that never include exception text, configuration values, paths,
  URLs, keys, or stack traces;
- continued fatal handling for an explicit Gemini selector and the retired Compose wrapper;
- non-destructive tolerance of stale Gemini child settings only after an explicit supported
  selector (`Mock`, `OpenAI`, `OpenAICompatible`, or `Ollama`);
- a synthetic contaminated-package regression beside the existing hermetic acceptance path;
- archive-local troubleshooting guidance.

Delivered files include:

- `backend/src/Taskdeck.Application/Services/RetiredLlmProviderConfigurationException.cs` (new)
- provider selection/registration, packaged runtime, and `Program.cs`
- focused Application/API tests
- archive harness and Python contract tests
- `docs/releases/WINDOWS_QUICK_START.md`

## Delivered verification

- Exact PR head `71f30964a5` passed the full backend solution: 8,094 tests passed with five
  intentional skips.
- Hosted exact-head CI completed with 22 successful checks and 11 intentional skips, including the
  dynamically scheduled E2E path.
- Independent review found no HIGH or CRITICAL blocker after the bounded fix round.
- The exact PR head started the seeded stack with `Llm__Provider=Mock` plus a synthetic inert
  `Llm__Gemini__ApiKey`; the API reached readiness and demo seeding completed without printing the
  synthetic value.
- The focused Application/API, archive, architecture, release-contract, docs-governance, Python
  compile, formatting, and marked-package diagnostic checks also passed before publication.

Not verified:

- a final no-publish candidate from the current post-`55dbf6e14` main;
- v0.1.2 tag workflows or public artifacts;
- ordinary public v0.1.2 Desktop launch.

## Preserved evidence and cleanup status

- The merged worktree remains tracked-clean at `71f30964a5` and contains ignored packaged proof under
  `artifacts/issue-1876-package-diagnostic/`. It may contain generated isolated identity state;
  do not commit or inspect it casually. Inventory and preserve required proof before any plain
  worktree removal; never force removal.
- Three exact synthetic diagnostic roots from the coordinator run remain under `%TEMP%` because the
  command safety floor rejected recursive removal even after read-only absolute-path and
  non-reparse verification. No process uses them:
  - `taskdeck-v011-exact-de58cdd8da7143e9bdccbef9188b4ac6`
  - `taskdeck-v011-clean-542a271cbb354fc0ade0b37d13921dea`
  - `taskdeck-v011-no-retired-gemini-4c7674a45f3e4122880da6dcae5b8823`
- The older release-proof worktree and its ignored evidence remain preserved separately.

## Current continuation

Do not resume the implementation branch or reopen `#1876`; the correction is already on main. Use
the live v0.1.2 checkpoint instead:

1. Refresh Git, GitHub, ProjectV2, CI, review threads, milestone membership, and worktrees.
2. Finish and merge the remaining Priority I milestone slices through their own exact-head gates.
3. From the resulting exact main, run the synthetic desktop receipt -> explicit Apply ->
   exactly-one-card journey and a blank-tag no-publish Windows candidate.
4. Assemble the release deck, including inherited-profile migration and ordinary
   Explorer/SmartScreen evidence or explicit unverified boundaries.
5. Do not create or push a v0.1.2 tag until the maintainer explicitly accepts that deck. The
   >=10-day dogfooding floor is the separate q-8 traction/archive checkpoint, not a v0.1.2 tag gate.

## User-facing status

- Usable at incident close: yes, through the compatibility launch; current process state must be
  rechecked rather than inferred.
- Safe to describe v0.1.1 as universally double-click-to-use: no.
- Public v0.1.2 available: no.
- Data or credentials deleted: no.
- Issue state: `#1876` closed as completed by PR `#2016`; implementation is on main, but no public
  v0.1.2 artifact exists.
