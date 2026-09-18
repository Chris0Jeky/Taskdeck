# v0.4 QA session and result template

Last Updated: 2026-09-10

Companion: [qualification plan](V04_QUALIFICATION_PLAN.md),
[feature contracts](../product/FEATURE_CAPABILITIES.md),
[existing manual rehearsal template](MANUAL_REHEARSAL_TEMPLATE.md).

## Run identity

- Session ID / date / operator / issue:
- Candidate full commit or tag / artifact digest / backend and frontend versions:
- Deployment (source / packaged Windows / self-host / controlled hosted):
- Browser and version / OS / physical device or emulation / assistive technology:
- Viewport / zoom / input method / experience / detail / selected and resolved theme:
- Provider mode (disabled / deterministic synthetic / configured live), model and effective limits:
- Fixture revision / account roles / board IDs / initial counts and revisions:
- Consent and evidence storage location (use synthetic or approved redacted data):
- Contract sources and any explicitly approved deviation from their expected behavior:

## Preconditions and reset

1. Pin and record the candidate. Use isolated data and the named fixture/reset command from the case.
2. Establish owner A, editor B, viewer C and unrelated D only where needed. Record actual permissions.
3. Record starting card/source counts, revisions, due dates, usage allowance and controlled clock.
4. Verify provider/configuration availability before a live case. Unavailable is BLOCKED, not a pass.
5. Restore the fixture between destructive/interference cases. Never reset a participant's real work.

## Outcome matrix

| Case / issue | Prerequisites and ordered actions | Expected UI | Expected persisted result | Expected requests / zero-effects | Actual observation | Result | Evidence / defect / retest |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Example, not executed | Record steps | State the visible result | State count/revision/content | State allowed writes/egress and what must remain unchanged | Leave blank until run | NOT RUN | Candidate-bound evidence |

Results: **PASS**, **FAIL**, **BLOCKED**, **NOT RUN**, **NOT APPLICABLE**. A blocked case names the
missing prerequisite and owner; not applicable cites a specific contract boundary. Neither counts
as a pass. Split partial results into individual cases instead of marking a mixed row successful.

## UX observations

Record task completion, elapsed time, assistance, mistaken actions, recovery attempts, participant
expectation and their explanation of the result. Separate observed behavior from interpretation.
Do not fabricate ease ratings or preference. Rotate experience order when comparing alternatives;
retain the same task fixture. A small qualitative session does not establish statistical superiority.

Record whether the user can explain: what saved, what is shared/private, what a proposal will change,
whether a model was called, what an error means, and the next safe action. Note distracting reminders,
missing feedback, confusing labels and hidden controls even when the underlying API is correct.

## Evidence and findings

- Keep sanitized screenshots, trace/log excerpts, measured counts and request receipts bound to case ID.
- Avoid tokens, credentials, private answers, raw participant recordings and production datasets.
- File a reproducible defect: candidate, fixture, steps, expected/actual, direct user impact and evidence.
- Reuse an existing owning issue when the failure is already tracked; link this session's new evidence.
- Retest the repair on its actual candidate and the affected interaction. Do not silently replace a fail.
- Performance targets come from the approved measured baseline in #2237; record raw values beforehand.

## Session closeout

- Executed / pass / fail / blocked / not-run / not-applicable counts, with denominator and coverage gaps:
- Actual platforms/configurations and combinations excluded, with reason:
- Confirmed trust/security/data-loss defects and disposition under existing release policy:
- Other defects, owner, severity, planned fix or explicit acceptance:
- Changed feature claims/limitations and links to corrected documentation:
- Next session and unresolved human/device/provider/hosting acceptance:

Do not infer release acceptance, production deployment authorization or participant preference.
The programme coordinates evidence; existing release and human decision gates remain authoritative.
