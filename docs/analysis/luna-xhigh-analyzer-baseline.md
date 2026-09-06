# Backend analyzer and format baseline: Taskdeck.Application

Measurement slice for #2352, recorded on `origin/main` base `16963a79b1357e284fd4e403e70f833925b3105e` in a guarded Windows worktree on 2026-09-06.

## Scope and repository policy

The measured project is `backend/src/Taskdeck.Application/Taskdeck.Application.csproj`. The repository currently has no root `.editorconfig`. `backend/Directory.Build.props` contains product-version metadata only; this slice found no central analyzer or format policy to ratchet. Frontend lint/typecheck ownership remains in frontend configuration and is outside this measurement.

Toolchain:

- .NET SDK `8.0.415`, MSBuild `17.11.48`.
- `dotnet format` `8.3.631204`.
- Windows `10.0.26200`, `win-x64`.

## Commands and results

| Command | Exit | Result |
| --- | ---: | --- |
| `dotnet restore backend/src/Taskdeck.Application/Taskdeck.Application.csproj` | 0 | Restored Domain and Application projects. |
| `dotnet build backend/src/Taskdeck.Application/Taskdeck.Application.csproj -c Release --no-restore --nologo` | 0 | 0 warnings, 0 errors. |
| `dotnet format backend/src/Taskdeck.Application/Taskdeck.Application.csproj --verify-no-changes --no-restore --verbosity normal` | 1 | 27 `WHITESPACE` diagnostics across 5 Application source files: `ArchiveRecoveryService.cs`, `AutomationPlannerService.cs`, `AutomationPolicyEngine.cs`, `ChatService.cs`, and `IArchiveRecoveryService.cs`. |
| `node scripts/check-docs-governance.mjs` | 0 | Passed. |
| `node scripts/check-golden-principles.mjs` | 0 | Passed. |
| `git status --porcelain` and `git diff --check` | 0 | No tracked source changes and no whitespace errors after measurement. |

The verify-only format invocation printed `Formatted code file` lines while returning the expected nonzero verification result; the final Git status remained clean, so no formatter output is part of this report’s source changes.

## Interpretation

This is a baseline, not a policy proposal or a repository-wide count. The Application project builds cleanly under the current configuration, while the current `dotnet format` rules would report 27 whitespace fixes in five files. The next safe step is to decide a narrowly compatible policy and ratchet from this measured set; this report intentionally does not add `.editorconfig`, `Directory.Build.props` analyzer settings, packages, suppressions, or formatting edits.
