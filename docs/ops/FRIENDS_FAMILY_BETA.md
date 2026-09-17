# Friends and family beta runbook

Last Updated: 2026-09-15

**Status: operator runbook for a small, trusted cohort.** This document does not authorize open
registration, a public hosted beta, new telemetry, or a new invite-code UI. It composes the shipped
Windows, private-instance, backup, registration, privacy, and review-first contracts into a path a
non-technical participant can follow.

Current product and release truth still comes from [`STATUS.md`](../STATUS.md),
[`strategy/PRODUCT_DIRECTION.md`](../strategy/PRODUCT_DIRECTION.md), and
[`REVIVAL_PLAN.md`](../REVIVAL_PLAN.md). The detailed private-instance deployment authority remains
[`STAGE1_PRIVATE_INSTANCE_RUNBOOK.md`](STAGE1_PRIVATE_INSTANCE_RUNBOOK.md); this runbook does not
replace or relax it.

## Recommendation

Start with **one maintainer-hosted private instance and one named participant** when the goal is a
guided friends-and-family session.

That path is recommended first because the participant only needs a browser, the maintainer can
update one instance, and both people can inspect the same board during the session. The cost is a
stronger privacy and operations burden: the maintainer controls the server, database, backups,
invite, provider configuration, and access policy. Use the exact two-account boundary and restore
proof in the Stage 1 runbook; this is not a general multi-user or open-registration path.

Use the **per-device Windows release** instead when the participant does not consent to the
maintainer hosting their data, does not need a shared board, or should retain sole control of their
workspace. It has a slightly longer first-run path, but the database and configuration stay on that
participant's computer by default.

Do not start with real family, school, health, employment, financial, client, or credential data.
The first journey uses synthetic content. Real data is admitted only after the participant has read
the privacy note, the chosen path has a tested backup, and the participant has seen how Review and
Apply work.

## Boundaries

- Cohort size for the hosted path: the maintainer plus **one named participant**. Wider cohorts use
  separate local Windows workspaces or wait for the separately governed hosted-beta path.
- Registration: `InviteOnly` only while the participant registers, then `Closed`.
- MFA remains disabled until its production at-rest secret contract is complete.
- Live LLM providers are optional. Mock/deterministic operation is the safer default for the first
  session.
- Findings from this cohort enter the dogfooding lane and are exempt from the normal intake severity
  floor, like the standing dogfooding tracker. Exemption permits recording and triage; it does not
  make every request urgent or authorize immediate implementation.
- Never put invite codes, API keys, access-policy identities, raw private captures, provider errors,
  or database contents into a GitHub issue or committed evidence.

Related safety references:

- [`BETA_THREAT_MODEL.md`](../security/BETA_THREAT_MODEL.md)
- [`MANAGED_KEY_USAGE_POLICY.md`](../security/MANAGED_KEY_USAGE_POLICY.md)
- [`TELEMETRY.md`](../TELEMETRY.md)
- [`UPGRADING.md`](../../UPGRADING.md)

## Path A — maintainer-hosted private instance

### A1. Complete the host preflight

Before contacting the participant, complete sections 1 through 5 of
[`STAGE1_PRIVATE_INSTANCE_RUNBOOK.md`](STAGE1_PRIVATE_INSTANCE_RUNBOOK.md):

1. create the instance secrets and a separate backup key;
2. start the production Compose stack;
3. create an encrypted backup;
4. place the instance behind an identity policy, not a public quick tunnel;
5. restore the backup into a fresh throwaway volume and record `integrity=ok`.

The hosted path is **not ready to invite** when any of these are true:

- `/health/ready` does not return 200 from inside the access boundary;
- the URL is reachable by an identity outside the policy;
- the exact running image identity is not recorded;
- no encrypted archive exists;
- the archive has not restored successfully into a fresh volume;
- the backup or connector key is stored beside the database or archive;
- registration is `Open`;
- demo credentials or demo data are being presented as the participant's workspace.

### A2. Prepare the cohort record

Create one **private shared note** controlled by the maintainer. This is the feedback channel for
this cohort; it deliberately does not require GitHub.

Create these headings before sharing it:

```text
Participant alias:
Invite date:
Taskdeck version / image identity:
Access path: hosted private instance
Privacy note acknowledged: yes / no / date
First capture:
First proposal shown:
First approval:
First Apply completed:
First return on another day:
Friction observed during the session:
Participant's own words:
Follow-up findings and links:
Withdrawal / deletion requested:
```

Use an alias in any repository evidence. Keep the participant's real identity, contact details, and
private feedback outside the repository.

### A3. Create the accounts in the safe order

1. Keep `TASKDECK_REGISTRATION_MODE=InviteOnly` while provisioning both named accounts.
2. Before opening the private URL, mint the seven-day first-owner invite from the private host
   shell as the non-root API user:

   ```bash
   docker compose -f deploy/docker-compose.yml --env-file deploy/.env exec --user 10001:10001 api \
     dotnet /app/cli/Taskdeck.Cli.dll invite create --expires 7
   ```

3. The maintainer opens the private URL, chooses **Register**, and uses that first-owner invite to
   create the owner account. Then mint one separate seven-day participant invite with the same
   non-root CLI command. Keep both generated codes private and use each only for its named account.
4. Send the private URL, participant invite code, and privacy note through a channel already trusted by both
   people. Do not put the code in the shared feedback note.
5. The participant registers using the invitee steps below.
6. Immediately after registration, set `TASKDECK_REGISTRATION_MODE=Closed`, recreate the stack, and
   run the valid-registration 403 probe specified in section 6 of the Stage 1 runbook. An empty-body
   400 response does not prove registration is closed.
7. Create a small synthetic board and grant the participant `Editor` access through **Workspace →
   Settings → Access** (`/workspace/settings/access`).
8. Before declaring the two-account boundary, confirm that the database volume is fresh or run an
   authoritative account inventory that includes existing users. Recording two successful
   registrations and the valid-registration 403 closure is not enough when an older volume may still
   contain active accounts. `GET /api/users` may support an inventory check, but it does not prove that
   exactly two users exist; stop and reconcile any additional identities before continuing.

Do not build or request a richer invite-code UI in this slice. Record invite UX friction as a
finding against the existing registration surface.

### A4. Invitee setup — browser-only, exact journey

Send the participant the following instructions without the operator-only sections around them.
Replace the bracketed values before sending.

> **Open Taskdeck**
>
> 1. Open `[PRIVATE TASKDECK URL]` in your normal browser.
> 2. Complete the private access screen using the identity the maintainer approved.
> 3. On the Taskdeck sign-in screen, choose **Register**.
> 4. Enter a username, email, and a unique password. Paste the one-time code into **Invite code**.
> 5. Select **Create account**. Do not send the code or password back to the maintainer.
>
> **Complete one safe test**
>
> 1. Open **Inbox** and select the shared synthetic board.
> 2. Capture: `Please create a card called family beta check for Friday.`
> 3. Select the capture and choose **Start Triage** (shown as **Ask AI** in Paper mode).
> 4. Wait for Taskdeck to produce a proposal. If it cannot, leave the capture in place and tell the
>    maintainer what the screen says; do not keep resubmitting it.
> 5. Open **Review**. Read the proposed change and its evidence before choosing **Approve**.
> 6. Return to the board and confirm that approval alone did **not** create the card.
> 7. In Review, choose **Apply to board**, then confirm **Apply** in the separate dialog.
> 8. Return to the board and confirm that exactly one `family beta check` card exists.
>
> **Record feedback**
>
> Add one sentence to the private shared note: what felt clear, what felt awkward, and anything you
> expected to happen differently. Do not paste passwords, invite codes, API keys, private captures,
> or unredacted screenshots into the note.

The safe path is always:

`Capture → proposal → Review → Approve → Apply to board → confirm Apply`

Approval is a decision, not a board mutation. The separate Apply confirmation is expected.

### A5. Optional live LLM provider

Leave live providers off for the first session unless the participant has explicitly accepted the
following disclosure and the Stage 1 spending ceiling is configured.

#### Host-to-participant privacy note

Use this wording or a plainer equivalent:

> This is a private Taskdeck instance operated by me, not a hosted service run by an independent
> company. Your account and workspace are stored in a database and encrypted backups that I
> administer. As the host, I have technical access to those files even though I will not browse your
> work casually.
>
> By default, Taskdeck can use its deterministic/mock path and no captured content is sent to an LLM
> provider. If I enable OpenAI for this instance, text sent for live processing leaves this server
> under **my provider account and API key**. I pay that provider bill and the provider processes the
> submitted content under my account's terms and retention settings. Do not submit content you would
> not consent to sharing through that route.
>
> You may ask me to stop the instance, remove your access, export what is available, or delete your
> account data. Tell me before using real personal data if any part of this is unclear.

Before enabling a live provider:

1. follow section 7 of the Stage 1 runbook, including the token ceiling and provider budget alert;
2. give the participant the disclosure above and record the acknowledgement date privately;
3. show the participant the instance's `GET /api/privacy/egress` disclosure;
4. use a provider key created for this instance, never a personal all-purpose key;
5. disable the provider and recreate the stack if the agreed spending threshold is breached.

The maintainer's key means the maintainer pays. It does **not** make the participant's content local,
and it does not make the participant the provider-account owner.

## Path B — participant-owned Windows workspace

Use this path for a Windows 10/11 x64 participant who should own the data locally and does not need a
shared board.

### B1. Operator preparation

1. Choose the exact release together; do not tell the participant to download an unspecified
   "latest" build.
2. Send the official release-page link, the exact ZIP filename, and the matching `.sha256` filename.
3. Send [`WINDOWS_QUICK_START.md`](../releases/WINDOWS_QUICK_START.md) as the canonical install,
   backup, upgrade, and troubleshooting guide.
4. Create the same private shared feedback note described in A2, changing `Access path` to
   `participant-owned Windows workspace`.
5. Do not ask the participant to expose the local listener, copy their database to the maintainer,
   or install a live provider for the first session.

### B2. Invitee setup — exact journey

> **Install**
>
> 1. Download the exact Taskdeck Windows ZIP and its `.sha256` file from the official release page.
> 2. Open PowerShell in the download folder and run the checksum commands in the Windows quick
>    start. Stop if the checksum does not match.
> 3. Right-click the ZIP and choose **Extract All**. Do not run Taskdeck from inside the ZIP.
> 4. Double-click **Taskdeck.Api.exe** in the extracted folder.
> 5. If SmartScreen appears, use **More info → Run anyway** only after the checksum passed and the ZIP
>    came from the official release page.
> 6. Keep the console window open. Wait for `Taskdeck is ready at http://127.0.0.1:<port>` and use the
>    browser window Taskdeck opens. The address is local to this computer.
> 7. Choose **Register** and create the local account. There is no invite code for this local path.
>
> **Try the review gate**
>
> 1. Create a blank synthetic board.
> 2. Open **Inbox** and capture: `Please create a card called local family beta check.`
> 3. Select the capture and choose **Start Triage** (shown as **Ask AI** in Paper mode).
> 4. Open **Review**, inspect the proposal, and choose **Approve**.
> 5. Confirm the board is still unchanged.
> 6. Choose **Apply to board**, confirm **Apply**, and verify exactly one card appears.
>
> **Stop and protect the data**
>
> 1. Return to the console, press **Ctrl+C**, and wait for `Taskdeck stopped. You can close this
>    window.`
> 2. Before an update, copy the entire `%LOCALAPPDATA%\Taskdeck` folder as described in the Windows
>    quick start. Keep `taskdeck.db` and `appsettings.local.json` together.
> 3. Add feedback to the private shared note. Never send the database, configuration file, password,
>    provider key, or private workspace content unless a specific recovery process has been agreed.

On this path, the participant controls the files under `%LOCALAPPDATA%\Taskdeck`. The maintainer
cannot inspect or recover them unless the participant deliberately shares a backup. If the
participant enables OpenAI using their own process-scoped key, they pay and their submitted content
egresses under their provider account; follow the exact optional-provider section in the Windows
quick start.

## Feedback channel — private shared note, not GitHub

The cohort feedback decision is:

- one private shared note per participant or household;
- a trusted direct-message channel for urgent contact;
- the maintainer converts reproducible, non-sensitive findings into GitHub issues;
- the participant is never required to create a GitHub account.

Do not rely on an in-app feedback action for this cohort. Current builds do not provide a
non-GitHub cohort feedback sink, and a repository issue form would expose more project machinery
than a non-technical participant needs.

### What the participant records

Use this short form:

```text
What were you trying to do?
What happened?
What did you expect?
Could you continue, or were you blocked?
What screen were you on?
Approximate time and Taskdeck version:
Optional redacted screenshot:
```

### What the maintainer records before opening an issue

- exact Taskdeck release, image ID, or commit;
- hosted or local path;
- whether Mock/deterministic or a verified live provider was active;
- synthetic reproduction steps where possible;
- whether data was lost, duplicated, exposed, or merely displayed incorrectly;
- whether the participant could continue safely;
- redacted logs or screenshots only;
- the participant's own description, paraphrased when necessary to remove private content.

A suspected security exposure, data loss, cross-user access, secret leak, or uncertain write goes
through private direct contact first. Stop the affected path, preserve the database/log evidence,
and use the repository's private security-reporting route rather than a public issue.

## Activation and success ledger

This cohort uses manual, consented observation. Do not claim invisible telemetry. Record timestamps
or `not reached` for each participant:

| Milestone | Evidence | Target |
| --- | --- | --- |
| Access established | Account created or local app opened | Participant reaches Home without maintainer control of their mouse/keyboard |
| First capture | Synthetic capture appears in Inbox | Participant can explain what they submitted |
| First proposal | Review shows a proposed board change | Proposal is inspectable and names the intended board/change |
| First approval | Proposal reaches Approved | Participant understands that the board is still unchanged |
| First Apply | Separate Apply confirmation succeeds | Exactly one intended board change exists |
| First independent loop | Participant repeats capture → Review → Apply | No step-by-step prompting |
| Return use | Participant opens Taskdeck on another day | Records a real intended use or explains why they did not use it |

Also record:

- time from opening the URL/app to first successful Apply;
- number of captures attempted;
- proposals approved, corrected, rejected, failed, and applied;
- points where the maintainer had to take control;
- nearly-used moments where the participant chose another tool instead;
- whether the participant would use the same path again next week.

These figures are cohort evidence, not product telemetry and not performance benchmarks.

## Weekly five-minute check-in

Ask once after five to seven days. Record the answers in the private note.

```text
1. What did you actually capture in Taskdeck this week?
2. What did you consider capturing but put somewhere else, and why?
3. Which proposal or review step felt unclear?
4. Did Taskdeck create, omit, duplicate, or misplace anything?
5. Where did you get stuck or need the maintainer?
6. What is the one change that would most increase the chance you use it next week?
7. Is there any data you want exported or deleted?
```

A week with no use is a result. Record it rather than skipping the check-in.

## Finding intake and triage

Friends-and-family observations route through the standing dogfooding lane represented by
[`dogfooding/LOG.md`](../dogfooding/LOG.md).

1. Record the observation immediately in the private note.
2. Reproduce with synthetic data where possible.
3. Classify the consequence: trust/security, data correctness, blocked core loop, serious friction,
   minor friction, or feature request.
4. Open or update a GitHub issue only after removing participant identity and private content.
5. State that the source is a friends-and-family dogfooding observation and link only non-sensitive
   evidence.
6. The normal intake severity floor does not prevent recording the finding. Existing review,
   ownership, milestone, authority, and release rules still decide whether and when it is worked.
7. Do not convert one participant preference into a general product requirement without repeated
   evidence.

## Exit, withdrawal, and incident procedure

### Normal end of the hosted trial

1. Ask whether the participant wants an available export before removal.
2. If the participant requests an export, they must run it from their own authenticated session
   before deletion. The current UI does not provide an account-export control, so use the browser's
   same-origin DevTools console:

   ~~~javascript
   const token = localStorage.getItem('taskdeck_token')
   const response = await fetch('/api/account/export', {
     headers: { Authorization: 'Bearer ' + token },
   })
   const blob = await response.blob()
   const url = URL.createObjectURL(blob)
   const link = document.createElement('a')
   link.href = url
   link.download = 'taskdeck-account-export.json'
   link.click()
   URL.revokeObjectURL(url)
   ~~~

   Keep the downloaded file private. It is account-scoped and does not include the full shared-board
   column/card/label/comment tree; do not send it to the maintainer unless the participant explicitly
   consents to that transfer.
3. Do not treat account deletion as a maintainer action. If the participant requests deletion, they
   must perform the supported authenticated request from their own session, using their current
   password and the exact confirmation phrase `DELETE MY ACCOUNT` at `POST /api/account/delete`.
   The participant can perform that request from their own authenticated same-origin browser session:

   ~~~javascript
   const currentPassword = window.prompt('Enter your current password locally; never share it') ?? ''
   const token = localStorage.getItem('taskdeck_token')
   const response = await fetch('/api/account/delete', {
     method: 'POST',
     headers: { Authorization: 'Bearer ' + token, 'Content-Type': 'application/json' },
     body: JSON.stringify({ currentPassword, confirmationPhrase: 'DELETE MY ACCOUNT' }),
   })
   console.log(response.status, await response.text())
   ~~~

   Run it only in that participant's own session, do not paste the token or password into a shared
   note, and expect HTTP 200 before signing out. Never ask the participant to send a password or
   token. If they do not perform that request, remove
   their board access, keep registration `Closed` until any unused invite expires (there is no
   invite-revocation operation), and stop the tunnel or other perimeter exposure; record that the
   account was retained rather than claiming it was deleted.
   Account deletion is not an unconditional erasure of every stored copy: automation proposals and
   MFA material can remain under the documented deletion gaps, and the Stage 1 schedule keeps up to
   12 weekly encrypted off-platform backups (about 90 days). Tell the participant these retention
   limits before deletion and do not claim that all linked records or backups disappeared.
4. Take a final encrypted backup only when its retention was disclosed and agreed.
5. Stop exposure or keep registration `Closed`; do not reopen registration while an unused invite is
   still valid.
6. Record the exit reason and whether the participant would return after a named change.

### Normal end of a local Windows trial

The participant owns the local workspace. Give them the uninstall/data-location steps from the
Windows quick start and do not retain a copy unless they explicitly request recovery help.

### Security, data-loss, or uncertain-write incident

1. Stop the affected action; for hosted exposure, stop the tunnel or identity application first.
2. Do not retry a potentially committed write until the current state is inspected.
3. Preserve the exact database, configuration, recovery files, logs, release identity, and time of
   observation without posting them publicly.
4. Contain any exposed invite by keeping registration `Closed` until it expires; do not claim that it
   was revoked because Taskdeck has no invite-revocation operation. Rotate or revoke any exposed
   provider key, API key, or access-policy credential using its supported mechanism.
5. Tell the participant plainly what is known, what is uncertain, and what was contained.
6. Use the private security-reporting path for a security concern; otherwise create a redacted issue
   from synthetic reproduction evidence.

## Operator completion checklist

The runbook is ready for a specific participant only when every applicable box is checked:

- [ ] Chosen path and exact Taskdeck version/image identity recorded.
- [ ] Participant received the plain-language privacy note.
- [ ] Private shared feedback note created; no secrets stored in it.
- [ ] Hosted path: Stage 1 backup and fresh-volume restore drill passed.
- [ ] Hosted path: outside identity denied; first-owner and participant invites minted separately.
- [ ] Hosted path: maintainer registered first; participant registered second.
- [ ] Hosted path: registration changed to `Closed` and the valid 403 probe passed.
- [ ] Local path: release ZIP checksum verified before execution.
- [ ] Synthetic capture → Review → Approve → Apply journey completed.
- [ ] Participant observed that approval alone did not mutate the board.
- [ ] Activation timestamps and intervention points recorded.
- [ ] Weekly check-in date scheduled.
- [ ] Exit, export, deletion, and incident contact explained.

## Verification record required for GH-1325

Document publication alone does not complete GH-1325. A clean-container or clean-VM agent simulation
may be used as a preflight to check commands and the invitee path, but it cannot close GH-1325 or
substitute for participant consent. Before closing it, attach a redacted record of a moderated,
end-to-end walkthrough with:

- the maintainer plus a real named participant under the hosted two-account boundary.

The record must identify the exact release/image, prove the backup/restore and registration-closure
steps where applicable, complete the synthetic capture → Review → Approve → Apply journey, and state
which instructions were corrected after the walkthrough. The maintainer must explicitly accept the
redacted record and its findings before closing the issue; no agent may infer that acceptance. Keep
the issue open when a real participant walkthrough or that explicit acceptance is missing. Never
include personal identities, invite codes, secrets, raw private captures, or provider payloads.
