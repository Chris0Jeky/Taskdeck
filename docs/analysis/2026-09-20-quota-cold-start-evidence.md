# Quota cold-start investigation (#1435)

## Observed negative control

PR #3280, head `5686a1e569e28ba63242316209cc3cd36e12dbcc`, ran against main `ecaccb0d2090c19a93908ee874b4600c0a6f6582` in synthetic merge `c78fb17e2434280fcab33bc8d4443f5a6aba9cf0`. The [Ubuntu API job](https://github.com/Chris0Jeky/Taskdeck/actions/runs/35481612562/job/106000432803) compiled and executed the diagnostic: four simultaneous first accesses to `WebApplicationFactory.Services` returned four distinct database paths. Its one-database assertion failed before admission counts were interpreted. The complete API result was 3319 passed, one failed (the diagnostic), and four skipped (the historical quota boundary tests). The independent fresh-file quota theory passed in that run.

The original issue's global-lock experiment assumes every reservation uses one database. This observation falsifies that assumption for the reproduced harness shape: `ConfigureWebHost` allocates a fresh database per host configuration, and competing lazy first accesses can construct multiple hosts. Serializing subsequent writes cannot turn separate databases into a shared quota. The observation is not proof of stale SQLite WAL visibility and does not justify a production startup warmer.

The two earlier diagnostic heads (`88888098` and `b6593896`) failed compilation due to missing or incorrect imports. They are authoring mistakes, not runtime evidence.

## Corrective change and independent coverage

Both fixture-based quota classes now capture one `IServiceProvider` before contenders start. They still create independent scopes, contexts and connections and still race reservation calls. All four quarantined request/token boundary tests are enabled again, including twelve four-way bursts per budget type. Barriers have bounded waits so an upstream test failure cannot hang the suite indefinitely.

The permanent database-identity regression asserts a shared physical database, exactly one allowed result, and exactly one persisted reservation. Separately, `LlmQuotaFreshFileConcurrencyTests` closes all setup connections before four dedicated contenders reserve on one explicitly known fresh SQLite WAL file, with pooling disabled and real migrations/pragma configuration. Six fresh files per budget test cover hourly requests, daily user tokens, and shared per-surface token budgets across distinct users. Both returned decisions and persisted rows are checked.

Production reservation SQL, application startup, settings, dependencies and schema are unchanged. Initializing a test host is not claimed as a production cold-start fix. The independent fresh-file tests prevent the host-initialization correction from being the only evidence about SQLite.

## Scope of the conclusion

This is same-process, independent-connection evidence, not a cross-process stress qualification or an exhaustive proof for every provider/configuration. The first observed failure explains the reproduced host-race mechanism, not every historical run whose database identities were not recorded. The restored boundary tests and all other checks must pass at the final PR head; an earlier head's results cannot qualify later changes. Current Linux/Windows status and any remaining findings are recorded on the PR.
