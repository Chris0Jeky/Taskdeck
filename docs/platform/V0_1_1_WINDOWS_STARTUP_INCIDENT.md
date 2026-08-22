# v0.1.1 Windows Startup Incident And Recovery Checkpoint

- Status: open release blocker (`#1876`)
- Recorded: 2026-08-22
- Public release: [`v0.1.1`](https://github.com/Chris0Jeky/Taskdeck/releases/tag/v0.1.1)
- Incident evidence: [`#1876` comment](https://github.com/Chris0Jeky/Taskdeck/issues/1876#issuecomment-5380396057)

## Executive summary

The public v0.1.1 ZIP is not corrupt, but the ordinary double-click experience is not reliable on
an upgrading Windows profile. On the maintainer's profile, both Downloads and Desktop extractions
failed before opening a listener because the process inherited a user-scoped retired Taskdeck
Gemini selector and retired `Llm__Gemini__*` settings. The packaged top-level catch discarded the
existing fixed migration guidance and showed only a generic port/data-folder error.

The earlier public-artifact acceptance used an intentionally hermetic child environment that
removed all inherited `Llm__*` names. That remains valid evidence for clean install, persistence,
review-first OpenAI behavior, and archive integrity, but it did not test the ordinary inherited
upgrade environment and must not be generalized to that path. Issue `#1876` is reopened.

A temporary compatibility launch of the unchanged public executable is working. The persistent
non-secret user provider selector has been changed explicitly from Gemini to OpenAI; all
credential-bearing Gemini and OpenAI variables remain present and were not read, copied, printed,
or deleted. A v0.1.2 correction is drafted and strongly focused-tested, but it is uncommitted,
unpublished, and not yet through the full backend, PR, CI, review, no-publish, or public-release
gates.

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

## Current user-level configuration boundary

- User-scoped `Llm__Provider` is now `OpenAI`.
- Four user-scoped `Llm__OpenAi__*` names remain present.
- Four user-scoped `Llm__Gemini__*` names remain present.
- No value was printed or retained in evidence.
- v0.1.1 still rejects the remaining retired Gemini section even after a supported provider is
  selected. The drafted v0.1.2 behavior would treat those keys as inert only after an explicit
  supported selector; it would still fail closed for `Provider=Gemini` and the retired Compose
  presence marker.

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

## Draft permanent correction (not shipped)

Preserved worktree:

`<Taskdeck repo>\.worktrees\codex-1876-desktop-retired-provider-diagnostics`

Branch and base:

- branch `issue-1876/desktop-retired-provider-diagnostics`
- base/HEAD `0f38c692c1a08f62451012cc17281348b9bf6d46`
- no commit, push, or PR

The draft adds:

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

Tracked draft files:

- `backend/src/Taskdeck.Application/Services/RetiredLlmProviderConfigurationException.cs` (new)
- provider selection/registration, packaged runtime, and `Program.cs`
- focused Application/API tests
- archive harness and Python contract tests
- `docs/releases/WINDOWS_QUICK_START.md`

## Draft verification already completed

- Application focused tests: 56/56 passed.
- API focused tests: 89/89 passed.
- Archive Python tests: 34/34 passed.
- Gemini-retirement architecture tests: 4/4 passed.
- Release structure/contract checks: 61/61 passed under Git Bash.
- Docs governance passed.
- Python compile passed.
- Scoped formatting and `git diff --check` passed.
- Actual self-contained marked-package diagnostic passed: exit 1, exact retired-provider code and
  static guidance, no synthetic secret, raw exception, stack, URL, ready marker, or listener.

Not verified:

- full `backend/Taskdeck.sln` test run (interrupted when implementation was paused);
- independent review;
- PR exact-head CI;
- no-publish release rehearsal;
- v0.1.2 tag workflows or public artifacts;
- ordinary public v0.1.2 Desktop launch.

## Preserved evidence and cleanup status

- The draft worktree contains ignored packaged proof under
  `artifacts/issue-1876-package-diagnostic/`. It may contain generated isolated identity state;
  do not commit or inspect it casually. Inventory ignored output before any worktree removal.
- Three exact synthetic diagnostic roots from the coordinator run remain under `%TEMP%` because the
  command safety floor rejected recursive removal even after read-only absolute-path and
  non-reparse verification. No process uses them:
  - `taskdeck-v011-exact-de58cdd8da7143e9bdccbef9188b4ac6`
  - `taskdeck-v011-clean-542a271cbb354fc0ade0b37d13921dea`
  - `taskdeck-v011-no-retired-gemini-4c7674a45f3e4122880da6dcae5b8823`
- The older release-proof worktree and its ignored evidence remain preserved separately.

## Exact resume point

In the preserved draft worktree, first re-run its guard, then:

```powershell
$env:Llm__Provider='Mock'
$env:TaskdeckMigration__RetiredLlmProviderConfigurationPresent='false'
dotnet test backend/Taskdeck.sln -c Release -m:1
```

If green:

1. Review the complete diff and inspect `git status --porcelain --ignored` without deleting proof.
2. Amend ADR-0055 and active configuration/upgrade docs to distinguish explicit retired selection
   from inert stale child keys after an explicit supported selector.
3. Commit with DCO, push, and open a ready PR closing `#1876`.
4. Run the bounded independent review, exact-head CI, and three-minute head-age gate.
5. Merge only if green, then run a blank-tag no-publish desktop rehearsal.
6. Tag and publish v0.1.2 only after the candidate works with both hermetic clean state and a
   synthetic contaminated inherited environment.
7. Download the unchanged public v0.1.2 ZIP and prove the ordinary Desktop path with the persisted
   explicit OpenAI selector and untouched inert Gemini variable names.

## User-facing status

- Usable now: yes, through the currently running compatibility instance.
- Safe to describe v0.1.1 as universally double-click-to-use: no.
- Public v0.1.2 available: no.
- Data or credentials deleted: no.
- Issue state: `#1876` reopened, Priority I, project Pending at the last successful read.
