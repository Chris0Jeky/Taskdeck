# Downloadable v0.3.0 launch kit

Last verified: 2026-09-02

This is a **maintainer-only posting kit** for the downloadable v0.3.0 release.
It is not a launch announcement, release schedule, hosted-service promise, or
authorization to post from any account. Re-check every time-sensitive link and
the release tag immediately before publication.

## Claim ledger

Only make a public claim when its evidence is in this ledger. “Last verified”
means this document's source check, not a new end-user or production exercise.
The drafts and probe answers below inherit this ledger; narrow or remove a
sentence that cannot inherit one of these shipped sources.

| Claim allowed in this kit | Shipped evidence | Owner | Last verified |
| --- | --- | --- | --- |
| Windows users can verify, extract, and double-click a portable ZIP; the listener remains local. | [Shipped ZIP/provenance receipt](https://github.com/Chris0Jeky/Taskdeck/blob/dcd258af262a0b7179b58ac3fb36f744f92255da/docs/STATUS.md#L31-L35) and [Windows quick start](../releases/WINDOWS_QUICK_START.md) | Release maintainer | 2026-09-02 |
| Self-hosters can run the supported Compose baseline. | [Shipped container baseline](https://github.com/Chris0Jeky/Taskdeck/blob/dcd258af262a0b7179b58ac3fb36f744f92255da/docs/STATUS.md#L873-L873) and [README Compose instructions](../../README.md) | Release maintainer | 2026-09-02 |
| A workspace is local SQLite data the operator controls; back up its accompanying local configuration/keys too. | [Shipped local-first direction](https://github.com/Chris0Jeky/Taskdeck/blob/dcd258af262a0b7179b58ac3fb36f744f92255da/docs/STATUS.md#L354-L354), [README local-first ownership](../../README.md), and [upgrade guide](../../UPGRADING.md) | Operator | 2026-09-02 |
| Captured text can become source-linked proposals; the review/apply loop is a separate, explicit user decision. | [Live-verified proposal loop](https://github.com/Chris0Jeky/Taskdeck/blob/dcd258af262a0b7179b58ac3fb36f744f92255da/docs/STATUS.md#L105-L105) | Product maintainer | 2026-09-02 |
| Untouched v0.3 builds have no automatic usage ping, crash reporter, update check, analytics script, or background destination. Configured LLMs, connectors, webhooks, login, Sentry, and OTLP are separate, user/operator-enabled egress. | [Shipped v0.3 telemetry statement](https://github.com/Chris0Jeky/Taskdeck/blob/dcd258af262a0b7179b58ac3fb36f744f92255da/docs/STATUS.md#L55-L55) and [telemetry policy](../TELEMETRY.md) | Release maintainer | 2026-09-02 |
| Agent-originated board changes are review-first: proposal, review, approval, then a separate Apply confirmation. | [Shipped end-to-end receipt](https://github.com/Chris0Jeky/Taskdeck/blob/dcd258af262a0b7179b58ac3fb36f744f92255da/docs/STATUS.md#L121-L121) and [Windows quick start](../releases/WINDOWS_QUICK_START.md) | Product maintainer | 2026-09-02 |
| Encrypted backup/restore and connector verification exist for the supported Docker deployment. The recovery objectives are objectives, not measured guarantees. | [Shipped recovery receipt](https://github.com/Chris0Jeky/Taskdeck/blob/dcd258af262a0b7179b58ac3fb36f744f92255da/docs/STATUS.md#L39-L39), [PR #2360](https://github.com/Chris0Jeky/Taskdeck/pull/2360), [PR #2361](https://github.com/Chris0Jeky/Taskdeck/pull/2361), and [disaster-recovery runbook](../ops/DISASTER_RECOVERY_RUNBOOK.md) | Recovery operator | 2026-09-02 |
| Windows ZIP checksums are published; the current ZIP is unsigned. | [Shipped ZIP/checksum receipt](https://github.com/Chris0Jeky/Taskdeck/blob/dcd258af262a0b7179b58ac3fb36f744f92255da/docs/STATUS.md#L31-L35), [published-artifact journey](https://github.com/Chris0Jeky/Taskdeck/blob/dcd258af262a0b7179b58ac3fb36f744f92255da/docs/STATUS.md#L121-L121), and [Windows quick start](../releases/WINDOWS_QUICK_START.md) | Release maintainer | 2026-09-02 |
| The core is GPL-3.0-only; earlier MIT releases retain the grants already made. | [Shipped licensing record](https://github.com/Chris0Jeky/Taskdeck/blob/dcd258af262a0b7179b58ac3fb36f744f92255da/docs/STATUS.md#L281-L281), [licensing follow-up](https://github.com/Chris0Jeky/Taskdeck/blob/dcd258af262a0b7179b58ac3fb36f744f92255da/docs/STATUS.md#L366-L366), [licensing policy](../../LICENSING.md), [GPL text](../../LICENSE), and [ADR-0050](../decisions/ADR-0050-gplv3-copyleft-core.md) | Maintainer/legal owner | 2026-09-02 |

## Posting boundary and release facts to re-check

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
  them, route public questions to [Taskdeck Issues](https://github.com/Chris0Jeky/Taskdeck/issues),
  not to a nonexistent Discussion category.

## Maintainer-voice drafts

### r/selfhosted

**Title:** Taskdeck: a local-first review queue for turning notes into action

Taskdeck is a local-first workspace for people who want action items to go
somewhere without handing an AI the keys to their board.

For Windows, download the ZIP from the release page, verify its SHA-256,
extract it, and double-click `Taskdeck.Api.exe`. For Docker:

```bash
docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile baseline up -d --build
```

Your workspace lives in SQLite data you control. Keep its local configuration
and encryption keys with your backups; the [upgrade guide](../../UPGRADING.md)
has the exact boundary.

Taskdeck sends nothing home in its untouched v0.3 configuration: no usage
ping, crash reporter, update check, or analytics. If you configure an LLM,
connector, webhook, Sentry, OTLP, or external login, those integrations can
send the data needed for the thing you chose to run; the full destination table
is in the [telemetry policy](../TELEMETRY.md).

The safety model is simple: AI/MCP board changes become proposals. You inspect
them in Review, approve them, and then Apply is a separate confirmation. A
proposal is not a board mutation.

The Windows artifact is currently unsigned, so SmartScreen may say “Windows
protected your PC.” Only continue after downloading from the official release
and verifying the published SHA-256; do not turn SmartScreen off globally.

Known limits are below. Questions and bugs go to the [issue tracker](https://github.com/Chris0Jeky/Taskdeck/issues).

### Show HN

**Title:** Show HN: Taskdeck – local-first action items with a review gate for AI changes

Hi HN — I made Taskdeck because I wanted captured text to become actionable
without permitting an AI to silently edit my work. It turns capture into
source-linked proposals; I review, approve, and separately Apply them to a
board.

It runs locally from a Windows ZIP or the supported Docker Compose baseline.
The default build sends no background telemetry; configured providers and
connectors are opt-in egress, described in the [telemetry policy](../TELEMETRY.md).
Workspace data is SQLite data the operator owns, with recovery guidance in the
[upgrade guide](../../UPGRADING.md) and [recovery runbook](../ops/DISASTER_RECOVERY_RUNBOOK.md).

I would especially value reports about installation, the review flow, and
where the local-first boundary is unclear. The Windows ZIP is unsigned at this
time; verify its SHA-256 before running it. Please use the
[issue tracker](https://github.com/Chris0Jeky/Taskdeck/issues) for questions or bugs.

**First comment:**

The important caveat up front: this is downloadable/self-hosted software, not
a hosted product. It does not yet ingest audio or diarize speakers, artefact
extraction is not wired to a request path, and applying approved work remains
per proposal. Details and issue links are in the known-gaps section below.

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
checksum, extract it, and start the executable. For a container deployment,
use the supported Compose baseline. The workspace is local SQLite data that you
control, and the operator guides cover upgrades and recovery.

Privacy is a default, not a slogan: an untouched v0.3 build has no usage ping,
crash reporter, update check, or analytics script. A configured LLM, connector,
webhook, external login, Sentry, or OTLP endpoint can of course communicate
with the service the operator chose; read the exact [telemetry destination
table](../TELEMETRY.md) before enabling one.

The project is candid about its beta limits. The Windows ZIP is unsigned,
there is no hosted instance, and several product boundaries remain open. If
this workflow is useful, install it from the official release, verify the
checksum, and use the [issue tracker](https://github.com/Chris0Jeky/Taskdeck/issues)
to report what works or does not.

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
- Batch approval stops at Approved; applying is still per proposal. Do not
  describe batch execution as shipped.
- There is no hosted instance. Do not turn the v0.4 direction into a current
  availability claim.

## First 48 hours after a maintainer post

1. **Same day:** triage every credible bug report into a GitHub issue, label it
   with the observed impact, and acknowledge the report without asking for
   secrets, private workspace content, keys, or a database copy. Use the
   [security policy](https://github.com/Chris0Jeky/Taskdeck/security/policy)
   for a security concern.
2. **Same day:** answer installation and product questions on the issue tracker
   while Discussions remain disabled. If Discussions are later enabled, update
   this kit and route questions to the announced category instead.
3. **Fix boundary:** “same-day” promises same-day triage and issue creation —
   not a guaranteed same-day release. A same-day fix is considered only for a
   reproducible security problem, data-loss risk, or release-blocking
   regression with a safe, narrowly scoped patch and maintainer release
   authority. Everything else gets a tracked issue and an honest status.
4. **At 24 and 48 hours:** re-check the official release, checksum, and issue
   intake; publish no new capability claim unless it has a shipped source.

### Pinned known-issues text (use only if Discussions become enabled)

> **Known issues and questions**
>
> This is the downloadable/self-hosted release, not a hosted service. The
> Windows ZIP is currently unsigned; verify the official SHA-256 before you run
> it. Known limits include no audio ingestion/diarization, unwired artefact
> extraction, unencrypted TOTP seeds at rest, and per-proposal Apply. Please
> report reproducible bugs with redacted steps; never post secrets, keys, or
> private workspace data. We triage bugs to issues the same day, but do not
> promise every fix the same day.

Until Discussions are actually enabled, do not post this pinned text: the live
public channel is [Taskdeck Issues](https://github.com/Chris0Jeky/Taskdeck/issues).

## Before a maintainer publishes

- [ ] Confirm the final `v0.3.0` release exists and replace RC-specific links.
- [ ] Re-run the Windows ZIP checksum and start path; run the supported Compose
      path separately.
- [ ] Confirm the unsigned/SmartScreen wording against the actual release
      artifact; never claim signing or universal SmartScreen behaviour.
- [ ] Re-check the telemetry destination table and every issue-linked gap.
- [ ] Re-check whether Discussions are enabled; change the question channel
      only with direct repository evidence.
- [ ] Re-check awesome-selfhosted's live contribution criteria and project
      eligibility. Do not submit while the release-age criterion is unmet.
- [ ] Maintainer posts from their own accounts. Posting is intentionally
      unperformed by this documentation change.
