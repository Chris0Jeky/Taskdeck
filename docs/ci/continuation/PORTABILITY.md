# Exporting and adapting the portable kit

Date: 2026-09-10. Parent: [operations and stack guide](OPERATIONS.md).

## Export from committed source

The exporter reads immutable Git blobs from a full commit ID. It does not scan the working tree, include arbitrary files, run candidate commands, modify the source repository, or publish a package. Its explicit allowlist contains the provider-neutral core, generic adapter/CLI, GitHub reader/collector, examples and independent tests. Taskdeck workflows, policy, bespoke adapter and staging transformer are excluded.

```sh
node scripts/ci/smart-ci/continuation/tools/export-kit.mjs --repo /path/to/Taskdeck --commit FULL_REVIEWED_COMMIT_SHA --out /path/outside/Taskdeck/ci-continuation-kit
node /independently/trusted/Taskdeck/scripts/ci/smart-ci/continuation/tools/verify-export.mjs --dir /path/outside/Taskdeck/ci-continuation-kit
cd /path/outside/Taskdeck/ci-continuation-kit
node --test
node examples/demo.mjs
```

The destination must be a new directory outside the source repository, with an existing trusted parent. Missing/empty/non-regular source inputs, symbolic refs such as HEAD, unsafe output placement and size limits fail before copying. Files are preflighted from immutable objects; outputs use exclusive creation and a failed write removes only the newly created output directory. Export is bounded to 8 MiB total input and 1 MiB per input.

The package is private/non-published and dependency-free. Node 22+ and Git are required. The generated README contains commands usable independently of Taskdeck. The same generic manifest can model one application or a polyglot monorepo; see [adapter protocol](ADAPTERS.md).

## Identity and licence

LICENSE is copied byte-for-byte from the selected source commit. The existing Taskdeck LICENSE blob is `f288702d2fa16d3cdf0035b15a9fcbc552cd88e7` (GPL version 3 text); this change neither alters it nor grants a new licence/exception. No npm publication or new external repository is created.

`export-manifest.json` records source commit/tree and each file's Git blob (where applicable), byte count and SHA-256 checksum. Generated README/package metadata have no source blob. Repeating an export from identical input produces the same payload and manifest, regardless of dirty working-tree changes.

Verification detects missing, altered, extra, symlink and special files and checks bounded inventory/depth. It is **checksum integrity, not signature/authenticity verification**. An attacker who can replace both a payload and its manifest can replace the checksums. Preserve a trusted manifest/source revision separately. Verify a pristine export; generated configs/reports should be written outside it to avoid legitimate additions being reported as unexpected files.

Obtain the self-contained verifier independently from a reviewed source revision and run it before any export code. It imports only Node built-ins and reads the target as data. The bundled copy is for an already trusted export; executing an untrusted verifier is not verification.

## Other-repository adoption

Use the exported `cli.mjs init` with `--kind node`, `dotnet` or `python` and the immutable numeric ID of the destination repository. All starters are unreviewed. The command identity, working directory, dependency resolution, platform/runtime and transitive fixtures must be reconciled with actual CI before any narrowing. The planner reads reviewed config from a base commit, never candidate or dirty-worktree config.

Start with complete component suites. Separate shared libraries, generated contracts, process harnesses and integration journeys only where their input closure is understood. Retain a canonical selection floor and unknown-path full fallback. Run shadow comparisons against uncached full qualifications before enabling any selection. Local cross-repository fixture success is not a real second-project adoption trial.

The exported GitHub collector is metadata-only and read-only. Admission requires a separate protected provenance verifier, and the ledger requires a separately protected latest anchor. Copying this kit does not provision those services or authorise a required-check replacement. Carry Taskdeck's least-privilege and full-audit principles, not its lane names or hosted-runner assumptions.

## Validation

Final local repository-overlay suite: **357 passed, zero failed/skipped/cancelled**, Node 22.16.0/Linux. The portable export independently runs **309 tests**, also zero failures/skips/cancellations, without Taskdeck files. The manifest verifier reports 27 payload files. A standalone local smoke used an explicitly labelled temporary Git validation snapshot, not a claimed GitHub/release commit. The copied licence bytes match the existing repository's Git blob.

Export regressions cover byte reproducibility, ignored dirty sources, preserved licence bytes, source/output containment, overwrite refusal, symbolic refs, missing/symlink source, tampering/extra output and independent exported execution. The first nested-suite test inherited Node's parent test-runner IPC environment and produced no TAP output; it was corrected to isolate that environment and use an explicit TAP reporter. Initial failure and successful rerun are retained separately.

No Windows export trial, real second-repository rollout, signing, publication or after-deployment performance gain is claimed. Hosted configured-Node and independent/maintainer review remain required before merging the stack.
