# Attempt-aware GitHub execution observations

Date: 2026-09-10. Related: #2336. Parent: [engineering contract](README.md).

## Workflow and trust boundary

`ci-continuation-observe.yml` adds a small read-only observer when the existing CI workflow completes. It runs no product suite, changes no selection, publishes no required status, signs no task evidence and activates no reuse. A workflow_run integration starts through the default branch after merging; opening this PR is not a live-observer demonstration.

The checkout uses the observer's own `github.workflow_sha`, never the triggering head. Permissions are contents/actions read only. Checkout credentials are not persisted; automatic package-manager caching is explicitly disabled. No triggering artifact is downloaded, no head command executed and no release secret referenced. Actions retain immutable pins, job timeout is five minutes and report retention fourteen days. Artifact names include source and observer run/attempt IDs.

There is an extra small runner/job/storage cost. Measure it alongside avoided work; this is not free efficiency or a replacement for the historical estate tool.

## Collection protocol

The provider accepts only fixed-origin https://api.github.com GET endpoint builders. It rejects redirects, unsafe IDs/repository strings, non-success responses, malformed/interrupted/oversized bodies and excessive requests. Defaults: 100 requests, 8 MiB per body, 64 MiB aggregate, fifteen-second request timeout, ten attempts and fifty pages per attempt. Hitting limits fails visibly rather than silently truncating evidence. Tokens and raw provider error bodies are not copied into exceptions.

The collector binds numeric repository ID/full name, workflow ID/path, run ID, terminal status, event/head and attempt identities. Every exact attempt's jobs endpoint is paginated with stable counts; duplicates/short pages/job-to-run mismatches fail. A final run reread detects concurrent reruns. Earlier failed/cancelled attempts remain after a green retry.

Taskdeck's current CI workflow ID is `236855317`, path `.github/workflows/ci-required.yml`. Recreating the workflow requires a reviewed ID update. The generic CLI accepts another repository's verified ID/path.

```sh
# Token is supplied through GH_TOKEN, never a command argument.
node scripts/ci/smart-ci/continuation/tools/collect-github.mjs --repository OWNER/REPO --repository-id NUMERIC_ID --run RUN_ID --workflow-id WORKFLOW_ID --workflow-path .github/workflows/ci.yml --out NEW_REPORT.json
```

Optional --head binds the event's REST head; --summary appends bounded Markdown. Output must be a new file. Errors attempt to retain a small complete:false report and exit nonzero. Since this observer is not required, its failure is not a product verdict.

## Observation is not reusable proof

Reports are `ci.github-observation.v1`, `authority:none`, `checkoutVerified:false`. REST head_sha identifies run metadata, not necessarily the checked-out synthetic merge tree. A success conclusion does not prove the command, resolved environment, full test inventory, absence of allow-failure behaviour or trusted workflow definitions/callees.

Do not invent provenance flags from this report and pass them to assertProducer. Protected execution provenance, issuer/key custody and authoritative revocations need independent implementation/review before old results can satisfy Taskdeck qualification. A signed unverified claim remains unverified.

Only names, IDs, timestamps and conclusions are retained, not logs/source/PR bodies/artifacts/tokens. Markdown escapes links/images, HTML, mentions, delimiters and newlines. Names may still be sensitive metadata: retain repository access/retention expectations when exporting.

## Metric semantics

Known runner seconds are a lower bound when durations are missing. Aggregate seconds stay null in that case; empty jobs do not prove zero cost. Job span is elapsed time across job endpoints, not a computed DAG critical path. Queue is null without creation metadata; setup/test seconds, test counts and billing are not inferred from names or green exits.

Deduplicate repeated observations by repository/run/attempt/job IDs before weekly aggregation. Retain collection times and earlier failures. Distinguish unique execution from repeated reports and public runner time from private billing.

## Validation and rollback

Local combined suite: **309 passed, zero failed/skipped/cancelled**, Node 22.16.0/Linux. Fixtures cover 101 jobs/two pages, failure then successful retry, altered identities, pagination/rerun races, unknown measurements, budgets and rendering. One initial fixture expectation was corrected: its mutator invalidated one duration on EACH page, giving two unknown durations rather than one. The initial failure and passing rerun are retained separately. No production provider request was made from the network-isolated local runtime.

Configured-Node hosted checks and independent/maintainer reviews remain required. After merging, inspect a real observer run before claiming deployed validation. Rollback this observer/new modules only; required CI/canonical policy are unaffected. Observer absence is never product success.

## Primary references

Workflow events/default-branch/security semantics: https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows

Exact-attempt job API and pagination: https://docs.github.com/en/rest/actions/workflow-jobs

Workflow run metadata: https://docs.github.com/en/rest/actions/workflow-runs

The provider uses documented API version 2026-03-10; upgrades require review and contract tests.
