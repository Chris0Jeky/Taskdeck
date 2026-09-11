# Downloadable v0.3.0 launch kit

Last verified: 2026-09-02

This is a **maintainer-only posting kit** for the downloadable v0.3.0 release.
It is not a launch announcement, release schedule, hosted-service promise, or
authorization to post from any account. Re-check every time-sensitive link and
the release tag immediately before publication.

Copy-readiness check: 2026-09-07 (D-4(a) batch behavior, D-14/SC-8 public-home
decision, and anonymous-home availability refreshed).

## Claim ledger

Only make a public claim when its evidence is in this ledger. “Last verified”
means this document's source check, not a new end-user or production exercise.
The drafts and probe answers below inherit this ledger; narrow or remove a
sentence that cannot inherit one of these shipped sources.

| Claim allowed in this kit | Shipped evidence | Owner | Last verified |
| --- | --- | --- | --- |
| Windows users can verify, extract, and double-click a portable ZIP; untouched packaged defaults are loopback-only. | [Shipped ZIP/provenance receipt](https://github.com/Chris0Jeky/Taskdeck/blob/dcd258af262a0b7179b58ac3fb36f744f92255da/docs/STATUS.md#L31-L35) and [Windows quick start](../releases/WINDOWS_QUICK_START.md) | Release maintainer | 2026-09-02 |
| Self-hosters can run the supported Compose baseline. | [Shipped container baseline](https://github.com/Chris0Jeky/Taskdeck/blob/dcd258af262a0b7179b58ac3fb36f744f92255da/docs/STATUS.md#L873-L873) and [README Compose instructions](../../README.md) | Release maintainer | 2026-09-02 |
| A workspace is local SQLite data the operator controls; back up its accompanying local configuration/keys too. | [Shipped local-first direction](https://github.com/Chris0Jeky/Taskdeck/blob/dcd258af262a0b7179b58ac3fb36f744f92255da/docs/STATUS.md#L354-L354), [README local-first ownership](../../README.md), and [upgrade guide](../../UPGRADING.md) | Operator | 2026-09-02 |
| Captured text can become source-linked proposals; the review/apply loop is a separate, explicit user decision. | [Live-verified proposal loop](https://github.com/Chris0Jeky/Taskdeck/blob/dcd258af262a0b7179b58ac3fb36f744f92255da/docs/STATUS.md#L105-L105) | Product maintainer | 2026-09-02 |
| Untouched v0.3 builds have no automatic usage ping, crash reporter, update check, analytics script, or background destination. Configured LLMs, connectors, webhooks, login, Sentry, and OTLP are separate, user/operator-enabled egress. | [Shipped v0.3 telemetry statement](https://github.com/Chris0Jeky/Taskdeck/blob/dcd258af262a0b7179b58ac3fb36f744f92255da/docs/STATUS.md#L55-L55) and [telemetry policy](../TELEMETRY.md) | Release maintainer | 2026-09-02 |
| Agent-originated board changes are review-first: proposal, review, approval, then an explicit Apply confirmation. Single-proposal Apply remains a separate action. The API can execute a selected batch of already-Approved proposals, up to 500, and returns an independent outcome for each item without whole-batch rollback. The current Paper batch control covers live, non-deferred Approved proposals: shared-board proposals are eligible regardless of author, while boardless proposals require ownership by the signed-in reviewer. The server rechecks each item's access, status, policy, and approved-revision pin; a request with no executable item may collapse to 404/403. Batch approval remains narrower. | [Shipped batch endpoint contract](../../backend/src/Taskdeck.Api/Controllers/AutomationProposalsController.cs), [batch authorization checks](../../backend/src/Taskdeck.Application/Services/BatchProposalExecutionService.cs), [Paper eligibility boundary](../../frontend/taskdeck-web/src/composables/useBatchExecuteProposals.ts), [per-item receipt shape](../../backend/src/Taskdeck.Application/DTOs/AutomationProposalDtos.cs), [D-4(a) ruling](../STATUS.md#L848), and [Windows quick start](../releases/WINDOWS_QUICK_START.md) | Product maintainer | 2026-09-07 |
| Encrypted backup/restore and connector verification exist for the supported Docker deployment. The recovery objectives are objectives, not measured guarantees. | [Shipped recovery receipt](https://github.com/Chris0Jeky/Taskdeck/blob/dcd258af262a0b7179b58ac3fb36f744f92255da/docs/STATUS.md#L39-L39), [PR #2360](https://github.com/Chris0Jeky/Taskdeck/pull/2360), [PR #2361](https://github.com/Chris0Jeky/Taskdeck/pull/2361), and [disaster-recovery runbook](../ops/DISASTER_RECOVERY_RUNBOOK.md) | Recovery operator | 2026-09-02 |
| Windows ZIP checksums are published; the current ZIP is unsigned. | [Shipped ZIP/checksum receipt](https://github.com/Chris0Jeky/Taskdeck/blob/dcd258af262a0b7179b58ac3fb36f744f92255da/docs/STATUS.md#L31-L35), [published-artifact journey](https://github.com/Chris0Jeky/Taskdeck/blob/dcd258af262a0b7179b58ac3fb36f744f92255da/docs/STATUS.md#L121-L121), and [Windows quick start](../releases/WINDOWS_QUICK_START.md) | Release maintainer | 2026-09-02 |
| The core is GPL-3.0-only; earlier MIT releases retain the grants already made. | [Shipped licensing record](https://github.com/Chris0Jeky/Taskdeck/blob/dcd258af262a0b7179b58ac3fb36f744f92255da/docs/STATUS.md#L281-L281), [licensing follow-up](https://github.com/Chris0Jeky/Taskdeck/blob/dcd258af262a0b7179b58ac3fb36f744f92255da/docs/STATUS.md#L366-L366), [licensing policy](../../LICENSING.md), [GPL text](../../LICENSE), and [ADR-0050](../decisions/ADR-0050-gplv3-copyleft-core.md) | Maintainer/legal owner | 2026-09-02 |

## Posting boundary and release facts to re-check

- **Publication gate:** Before every maintainer post, and again after any
  private-repository cutover, resolve and record SC-8's public-home decision
  for the release assets and public-source messaging. While signed out, verify
  every ZIP and checksum download plus every documentation, support, and
  security URL used by the draft from that public home. Complete a real
  anonymous download of both the ZIP and its checksum and verify that pair.
  **Do not publish if no public home works for every required link.**
- The maintainer posts. This document does **not** authorize an agent or
  contributor to submit to Reddit, Hacker News, dev.to, GitHub, or any other
  channel.
- Post only after `v0.3.0` final exists as a published tag/release. At this
  verification point, `v0.3.0-rc.1` is a pre-release, not the final launch
  artifact. See the [RC notes](../releases/notes/v0.3.0-rc.1.md).
- This is downloadable self-hosted software. Do not say “hosted,” “sign up,”
  “we run your instance,” or otherwise turn the v0.4 hosted theme into a
  present-tense v0.3 claim.
- GitHub Discussions are disabled as of 2026-09-02. Until a maintainer enables
  Discussions or another public support route, keep the support destination as
  an explicit pre-post check; do not put a private-repository issue URL in an
  external draft.

### Approved public home for external drafts

SC-8/D-14 approved the public release/source mirror
<https://github.com/Chris0Jeky/taskdeck-release>, using a snapshot per release
with `ci/` and `scripts/ci/` stripped; the development repository remains the
private source of CI and issue operations. An unauthenticated check on
2026-09-07 returned HTTP 404 for the mirror root, its `/releases` page, and
`/blob/main/README.md`. The mirror is therefore the approved destination but is
not available yet: keep the publication gate closed, do not substitute the
development repository, and do not invent a tag-specific release or asset URL.
Before posting, the maintainer must make the mirror anonymous and then complete
the signed-out link and ZIP/checksum checks below.

### Mirror policy reachability gate

Do not post any external draft until the public mirror exposes a signed-out
telemetry-destination table and its security policy's private-disclosure route
has been tested end to end. The proposed CI-16 mirror allowlist does not include
`docs/TELEMETRY.md`, and the current `SECURITY.md` points its only active
reporting route at `Chris0Jeky/Taskdeck/security/advisories/new`, which becomes
private after cutover; its email fallback is explicitly inactive. `#2439` owns
the mirror implementation needed to export or otherwise provide those public
paths. This is a publication gate, not evidence that the launch is ready.

## Listener and network-binding boundary

Untouched packaged defaults are loopback-only; that statement does not survive
an explicit or inherited listener override. `urls` (including command-line
`--urls`) or `ASPNETCORE_URLS`; bare `HTTP_PORTS` / `HTTPS_PORTS`;
`DOTNET_HTTP_PORTS` / `DOTNET_HTTPS_PORTS`; their
`ASPNETCORE_HTTP_PORTS` / `ASPNETCORE_HTTPS_PORTS` variants; and
`Kestrel:Endpoints` settings may override packaged loopback selection and
expose Taskdeck. Do not use wildcard or non-loopback bindings unless exposure
is intentional and the operator has reviewed the resulting network boundary.

## Maintainer-voice drafts

### r/selfhosted

**Title:** Taskdeck: a local-first review queue for turning notes into action

Taskdeck is a local-first workspace for people who want action items to go
somewhere without handing an AI the keys to their board.

For Windows, download the ZIP from the [approved public release page](https://github.com/Chris0Jeky/taskdeck-release/releases), verify its SHA-256, extract it, and double-click `Taskdeck.Api.exe`. For Docker:

Follow the canonical [README Compose setup](https://github.com/Chris0Jeky/taskdeck-release/blob/main/README.md#2-docker): copy
`deploy/.env.example` to `deploy/.env`, then populate `TASKDECK_JWT_SECRET`
and `TASKDECK_CONNECTORS_ENCRYPTION_KEY` in `deploy/.env` before running:

```bash
docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile baseline up -d --build
```

Your workspace lives in SQLite data you control. Keep its local configuration
and encryption keys with your backups; the [upgrade guide](https://github.com/Chris0Jeky/taskdeck-release/blob/main/UPGRADING.md)
has the exact boundary.

Taskdeck sends nothing home in its untouched v0.3 configuration: no usage
ping, crash reporter, update check, or analytics. If you configure an LLM,
connector, webhook, Sentry, OTLP, or external login, those integrations can
send the data needed for the thing you chose to run. Do not post this draft
until the mirror exposes and the maintainer has signed-out verified the public
telemetry-destination table.

The safety model is simple: AI/MCP board changes become proposals. You inspect
them in Review, approve them, and then Apply is a separate confirmation. A
proposal is not a board mutation. Single-proposal Apply remains explicit. The
API can execute a selected batch of already-Approved proposals, up to 500 in
one request, with an independent `Applied`, `Skipped`, or `Failed` outcome for
each item and no whole-batch rollback. In the current Paper UI, batch Apply
covers live, non-deferred Approved proposals. Shared-board proposals are eligible
regardless of author; boardless proposals require ownership by the signed-in
reviewer. The server rechecks each item's access, status, policy, and
approved-revision pin. Batch Apply still requires explicit confirmation, accepts
up to 500 selected proposals, and a request with no executable item may collapse
to 404/403. Batch approval remains narrower.

The Windows artifact is currently unsigned, so SmartScreen may say “Windows
protected your PC.” Only continue after downloading from the official release
and verifying the published SHA-256; do not turn SmartScreen off globally.

**Known limits:** This is a single-node SQLite deployment; protect the database
and its local configuration and keys. The downloadable release has no audio
ingestion or speaker diarization, artefact extraction is not wired to a request
path, and MFA TOTP seeds remain unencrypted at rest. There is no hosted
instance. Questions and ordinary, non-security bugs should use the support
route linked from the [approved public mirror](https://github.com/Chris0Jeky/taskdeck-release)
once it is live. Suspected vulnerabilities must not be opened as a public
issue, discussion, or PR; use the [public security policy](https://github.com/Chris0Jeky/taskdeck-release/blob/main/SECURITY.md)
until coordinated disclosure. Do not post until that policy's private
disclosure route has been signed-out tested end to end; its email fallback is
not active.

### Show HN

**Title:** Show HN: Taskdeck – local-first action items with a review gate for AI changes

Hi HN — I made Taskdeck because I wanted captured text to become actionable
without permitting an AI to silently edit my work. It turns capture into
source-linked proposals; I review, approve, and separately Apply them to a
board.

It runs locally from a Windows ZIP or the supported Docker Compose baseline.
Use the [approved public release page](https://github.com/Chris0Jeky/taskdeck-release/releases)
and [README Compose setup](https://github.com/Chris0Jeky/taskdeck-release/blob/main/README.md#2-docker).
The default build sends no background telemetry; configured providers and
connectors are opt-in egress. Do not post this draft until the mirror exposes
and the maintainer has signed-out verified the public telemetry-destination
table.
Workspace data is SQLite data the operator owns; the [upgrade guide](https://github.com/Chris0Jeky/taskdeck-release/blob/main/UPGRADING.md)
covers the backup boundary.

I would especially value reports about installation, the review flow, and
where the local-first boundary is unclear. The Windows ZIP is unsigned at this
time; verify its SHA-256 before running it. Use the support route linked from
the [approved public mirror](https://github.com/Chris0Jeky/taskdeck-release) for
questions and ordinary, non-security bugs once it is live. Suspected
vulnerabilities must not be opened as a public issue, discussion, or PR; use the
[public security policy](https://github.com/Chris0Jeky/taskdeck-release/blob/main/SECURITY.md)
until coordinated disclosure. Do not post until that policy's private
disclosure route has been signed-out tested end to end; its email fallback is
not active.

**Known limits:** This downloadable, self-hosted release has no audio ingestion
or speaker diarization, artefact extraction is not wired to a request path, and
MFA TOTP seeds remain unencrypted at rest in its single-node SQLite data. There
is no hosted instance. Single-proposal Apply remains explicit. The API can
execute a selected batch of already-Approved proposals, up to 500, and reports
`Applied`, `Skipped`, or `Failed` independently for each item; the current Paper
UI covers live, non-deferred Approved proposals. Shared-board proposals are
eligible regardless of author; boardless proposals require ownership by the
signed-in reviewer. The server rechecks each item's access, status, policy, and
approved-revision pin. Batch Apply still requires explicit confirmation, accepts
up to 500 selected proposals, and a request with no executable item may collapse
to 404/403. Batch approval remains narrower.

**First comment:**

The important caveat up front: this is downloadable/self-hosted software, not
a hosted product. It does not yet ingest audio or diarize speakers, artefact
extraction is not wired to a request path, and MFA TOTP seeds remain unencrypted
at rest in the single-node SQLite data. Single-proposal Apply remains explicit.
The API can execute a selected batch of already-Approved proposals, up to 500,
and returns `Applied`, `Skipped`, or `Failed` for each item without rolling back
successful neighbours. The current Paper UI covers live, non-deferred Approved
proposals. Shared-board proposals are eligible regardless of author; boardless
proposals require ownership by the signed-in reviewer. The server rechecks each
item's access, status, policy, and approved-revision pin. Batch Apply still
requires explicit confirmation, accepts up to 500 selected proposals, and a
request with no executable item may collapse to 404/403. Batch approval remains
narrower.
Use the [approved public release page](https://github.com/Chris0Jeky/taskdeck-release/releases)
and [public security policy](https://github.com/Chris0Jeky/taskdeck-release/blob/main/SECURITY.md);
there is no hosted instance and the public support route is available only after
the mirror is live.

### dev.to

**Title:** Action items that go somewhere — and an AI that cannot touch your board without you

Most tools can collect notes. The hard part is carrying a useful action from a
messy source into a board without creating a black box that changes work behind
your back.

Taskdeck takes a deliberately narrower path. Capture text becomes
source-linked proposals. The proposal is visible in Review. Approval is not an
edit. Applying to the board is a separate, explicit confirmation. That is the
whole point: action items can go somewhere, while an AI cannot touch your board
without you.

Taskdeck is downloadable and self-hosted. On Windows, verify the official ZIP
checksum from the [approved public release page](https://github.com/Chris0Jeky/taskdeck-release/releases),
extract it, and start the executable. For a container deployment, use the
[supported Compose baseline](https://github.com/Chris0Jeky/taskdeck-release/blob/main/README.md#2-docker).
The workspace is local SQLite data that you control; the [upgrade guide](https://github.com/Chris0Jeky/taskdeck-release/blob/main/UPGRADING.md)
covers backups.

Privacy is a default, not a slogan: an untouched v0.3 build has no usage ping,
crash reporter, update check, or analytics script. A configured LLM, connector,
webhook, external login, Sentry, or OTLP endpoint can communicate with the
service the operator chose. Do not post this draft until the mirror exposes and
the maintainer has signed-out verified the public telemetry-destination table.

The project is candid about its beta limits. The Windows ZIP is unsigned,
there is no hosted instance, and several product boundaries remain open. If
this workflow is useful, install it from the [approved public release page](https://github.com/Chris0Jeky/taskdeck-release/releases),
verify the checksum, and use the support route linked from the [approved public
mirror](https://github.com/Chris0Jeky/taskdeck-release) for ordinary,
non-security problems once it is live. Suspected vulnerabilities must not be
opened as a public issue, discussion, or PR; use the [public security policy](https://github.com/Chris0Jeky/taskdeck-release/blob/main/SECURITY.md)
until coordinated disclosure. Do not post until that policy's private
disclosure route has been signed-out tested end to end; its email fallback is
not active.

**Known limits:** This downloadable, self-hosted release has no audio ingestion
or speaker diarization, artefact extraction is not wired to a request path, and
MFA TOTP seeds remain unencrypted at rest in the single-node SQLite data. There
is no hosted instance. Single-proposal Apply remains explicit; a separate
batch Apply can execute a selected set of already-Approved proposals, up to
500, and reports `Applied`, `Skipped`, or `Failed` independently for each item.
The current Paper UI covers live, non-deferred Approved proposals. Shared-board
proposals are eligible regardless of author; boardless proposals require
ownership by the signed-in reviewer. The server rechecks each item's access,
status, policy, and approved-revision pin. Batch Apply still requires explicit
confirmation, accepts up to 500 selected proposals, and a request with no
executable item may collapse to 404/403. Batch approval remains narrower.

### awesome-selfhosted — do not submit yet

**Status as of 2026-09-02: withheld; not eligible for submission.**

The authoritative [awesome-selfhosted-data contribution criteria](https://github.com/awesome-selfhosted/awesome-selfhosted-data/blob/master/CONTRIBUTING.md)
require a first release more than four months old, active maintenance, and
working installation instructions. Taskdeck's first release was published on
2026-08-19, so it does not meet the release-age criterion. Do not open a PR or
claim eligibility now. Re-check the criteria and project activity on the day of
a future maintainer submission.

When eligible, the maintainer can adapt this factual description (and must use
the target repository's current template/metadata format):

> Taskdeck — Local-first, self-hosted workspace that turns captured text into
> source-linked, reviewable proposals before board changes are applied.

**Mandatory caveat append (paste with the description when eligible):** This is
downloadable, self-hosted software with no hosted instance. It uses single-node
SQLite data that the operator controls; MFA TOTP seeds remain unencrypted at
rest. The release does not ingest audio or diarize speakers, and artefact
extraction is not wired to a request path. Single-proposal Apply remains
explicit. The API can execute a selected batch of already-Approved proposals,
up to 500, with an independent `Applied`, `Skipped`, or `Failed` outcome for
each item. The current Paper UI covers live, non-deferred Approved proposals.
Shared-board proposals are eligible regardless of author; boardless proposals
require ownership by the signed-in reviewer. The server rechecks each item's
access, status, policy, and approved-revision pin. Batch Apply still requires
explicit confirmation, accepts up to 500 selected proposals, and a request with
no executable item may collapse to 404/403. Batch approval remains narrower.
Use the
[approved public source and release mirror](https://github.com/Chris0Jeky/taskdeck-release)
and its [public security policy](https://github.com/Chris0Jeky/taskdeck-release/blob/main/SECURITY.md)
after the publication gate has passed.

## Probe-answer bank

### “Does it phone home?”

Not in an untouched v0.3 configuration. It has no usage ping, crash reporter,
update check, analytics script, or automatic destination. That does **not**
mean “cannot use the network”: an LLM provider, connector, webhook, external
login, Sentry, or OTLP endpoint communicates only when configured or used. The
[telemetry policy](../TELEMETRY.md) names each destination and the data boundary.

### “Will the licence change or take away my existing rights?”

The current core is GPL-3.0-only. Copies already received under the previous
MIT releases keep the MIT grants already given; retaining the old MIT text does
not dual-license the current project. GPL-covered modified distributions must
provide corresponding source as the licence requires. See
[LICENSING.md](../../LICENSING.md), [LICENSE](../../LICENSE), and
[ADR-0050](../decisions/ADR-0050-gplv3-copyleft-core.md). This is a project
policy summary, not legal advice.

### “What are the known gaps?”

- No audio ingestion or speaker diarization is part of this downloadable
  release. Do not imply otherwise.
- This is a single-node SQLite deployment, not a multi-node scale-out offer.
  The documented board-heavy k6 envelope uses shared 2-core runners, 20 VUs,
  and 90 seconds: median about 12 ms and board-write p95 2.0–3.0 s. A 2.0 s
  p95 is the near-capacity warning; 4.5 s is the tail gate. This is documented
  capacity, not a 100–500-user guarantee. See the [board-heavy k6 profile](https://github.com/Chris0Jeky/Taskdeck/blob/dcd258af262a0b7179b58ac3fb36f744f92255da/tests/load/k6/board-heavy-load.js#L5-L6),
  [shared-runner calibration](https://github.com/Chris0Jeky/Taskdeck/blob/dcd258af262a0b7179b58ac3fb36f744f92255da/tests/load/k6/board-heavy-load.js#L26-L30),
  [shipped capacity record](https://github.com/Chris0Jeky/Taskdeck/blob/dcd258af262a0b7179b58ac3fb36f744f92255da/docs/STATUS.md#L250-L252),
  [recalibration record](https://github.com/Chris0Jeky/Taskdeck/blob/dcd258af262a0b7179b58ac3fb36f744f92255da/docs/STATUS.md#L378-L378),
  [performance budgets](../PERFORMANCE_BUDGETS.md), and [ADR-0052](../decisions/ADR-0052-ci-estate-right-sizing.md).
- Artefact extraction is not connected to a request path yet ([#1429](https://github.com/Chris0Jeky/Taskdeck/issues/1429)).
- MFA TOTP seeds remain unencrypted at rest in SQLite until
  [#1653](https://github.com/Chris0Jeky/Taskdeck/issues/1653); protect the data
  file accordingly.
- Apply remains an explicit action for one proposal. The API's separate batch
  Apply path accepts a selected set of already-Approved proposals, up to 500
  per request, and returns `Applied`, `Skipped`, or `Failed` independently for
each item; there is no whole-batch rollback. The current Paper UI covers live,
non-deferred Approved proposals. Shared-board proposals are eligible regardless
of author; boardless proposals require ownership by the signed-in reviewer.
Batch Apply still requires explicit confirmation, accepts up to 500 selected
proposals, and reports independent outcomes without whole-batch rollback. The
server rechecks each item's access, status, policy, and approved-revision pin;
batch approval remains narrower.
- There is no hosted instance. Do not turn the v0.4 direction into a current
  availability claim.

## First 48 hours after a maintainer post

1. **Same day:** triage every credible ordinary, non-security bug report into
   a public GitHub issue, label it with the observed impact, and acknowledge
   the report without asking for secrets, private workspace content, keys, or
   a database copy. A suspected vulnerability must not become a public issue,
   discussion, or PR: route it privately through
   [SECURITY.md](../../SECURITY.md) until coordinated disclosure.
2. **Same day:** answer installation and product questions on the issue tracker
   while Discussions remain disabled. Do not continue a suspected vulnerability
   in that public channel; use the private [SECURITY.md](../../SECURITY.md)
   route until coordinated disclosure. If Discussions are later enabled, update
   this kit and route questions to the announced category instead.
3. **Fix boundary:** “same-day” promises same-day triage and public issue
   creation for ordinary, non-security reports — not a guaranteed same-day
   release. Suspected vulnerabilities stay off public issues, discussions, and
   PRs and use the private [SECURITY.md](../../SECURITY.md) route until
   coordinated disclosure. A same-day fix is considered only for a
   reproducible security problem, data-loss risk, or release-blocking
   regression with a safe, narrowly scoped patch and maintainer release
   authority. Everything else gets a tracked issue and an honest status.
4. **At 24 and 48 hours:** re-check the official release, checksum, ordinary
   public-issue intake, and the private [SECURITY.md](../../SECURITY.md)
   reporting route; publish no new capability claim unless it has a shipped
   source.

### Pinned known-issues text (use only if Discussions become enabled)

> **Known issues and questions**
>
> This is the downloadable/self-hosted release, not a hosted service. The
> Windows ZIP is currently unsigned; verify the official SHA-256 before you run
> it. Known limits include no audio ingestion/diarization, unwired artefact
> extraction, unencrypted TOTP seeds at rest, and explicit single/batch Apply.
> The API batch is bounded at 500 selected already-Approved proposals and
> reports an independent result for each item. The current Paper UI covers live,
> non-deferred Approved proposals. Shared-board proposals are eligible regardless
> of author; boardless proposals require ownership by the signed-in reviewer.
> Batch Apply still requires explicit confirmation, and the server rechecks each
> item's access, status, policy, and approved-revision pin. Please
> report reproducible non-security bugs with redacted steps; never post
> secrets, keys, or private workspace data. Suspected vulnerabilities must not
> be posted as a public issue, discussion, or PR; use the private
> [security policy](https://github.com/Chris0Jeky/taskdeck-release/blob/main/SECURITY.md)
> route until coordinated disclosure. We
> triage ordinary bugs to issues the same day, but do not promise every fix the
> same day.

Until Discussions or another public support route is actually enabled, do not
post this pinned text: the approved public home is the [release mirror](https://github.com/Chris0Jeky/taskdeck-release),
which is still unavailable at this verification point.

## Before a maintainer publishes

- [ ] Confirm the final `v0.3.0` release exists and replace RC-specific links.
- [ ] Resolve and record SC-8's public-home decision. After any private
      cutover, while signed out, open every ZIP/checksum, documentation,
      support, and security URL in the selected draft; complete a real
      anonymous ZIP/checksum download and verification. Do not publish unless
      every required link works from a public home.
- [ ] Re-run the Windows ZIP checksum and start path; run the supported Compose
      path separately after the canonical README setup: copy
      `deploy/.env.example` to `deploy/.env` and populate
      `TASKDECK_JWT_SECRET` and `TASKDECK_CONNECTORS_ENCRYPTION_KEY`.
- [ ] Confirm the unsigned/SmartScreen wording against the actual release
      artifact; never claim signing or universal SmartScreen behaviour.
- [ ] Re-check the telemetry destination table and every issue-linked gap.
- [ ] Confirm the public mirror exposes the telemetry-destination table and
      that the private disclosure link in its security policy works end to end
      while signed out. Do not publish if either path fails; `#2439` owns any
      mirror export or reporting-route implementation needed to make them work.
- [ ] Re-check whether Discussions are enabled; change the question channel
      only with direct repository evidence.
- [ ] Re-check awesome-selfhosted's live contribution criteria and project
      eligibility. Do not submit while the release-age criterion is unmet.
- [ ] Maintainer posts from their own accounts. Posting is intentionally
      unperformed by this documentation change.
