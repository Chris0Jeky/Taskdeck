# First-Run Workflows

This guide covers the shortest real workflows for the shipped novice-first shell.

Use [../START_HERE.md](../START_HERE.md) first if you want the quick orientation.
Use [HELP_AND_FAQ.md](HELP_AND_FAQ.md) when a page is confusing or unexpectedly empty.

## Workflow 1: Reach A Useful Board Fast

Use this when you are brand new to the app.

1. Open `Home`.
2. If there is no board yet, choose the setup action from `Home` or `Today`.
3. Name the board.
4. Pick one setup shape:
   - `Blank board` if you want to shape the workflow yourself
   - `Engineering sprint` for software delivery work
   - `Support triage` for incoming issue queues
   - `Content calendar` for editorial or publishing workflows
5. Open the new board.
6. Add one card directly or drop a note into `Inbox` if the work is still messy.

What success looks like:

- you have one board you can actually work in
- you know where `Review` sits before automation touches anything

## Workflow 2: Turn A Messy Note Into Board Work

Use this when the input is not yet structured enough to become a card by hand.

1. Start from `Home`, `Today`, or quick capture.
2. Save the note into `Inbox`.
3. Open the Inbox item and start triage.
4. Wait for the proposal to appear in `Review`.
5. Read the proposed operations carefully.
6. Approve and execute only if the change is correct.
7. Open the linked board and continue the work there.

What success looks like:

- the input was captured without losing context
- the board change happened through `Review`, not silently
- the resulting work is visible on the board

Common mistake:

- skipping straight to advanced `Queue` or `Chat` when `Inbox -> Review` would be simpler

## Workflow 3: Reset The Day

Use this when you already have work underway and need to decide what matters today.

1. Open `Home` to see captures, pending review, and recent boards.
2. Open `Today`.
3. Check the `Review queue` section first.
4. Check overdue, due-today, and blocked cards next.
5. Use the recommended actions to jump into `Review`, `Inbox`, or the relevant board.
6. Return to the board once the change is decided.

What success looks like:

- you did not have to guess which page mattered first
- pending proposals were decided before board work drifted
- blocked or overdue items were visible early

## Workflow 4: Recover When The Loop Feels Unclear

Use this when you are unsure what to click next.

1. Go back to `Home`.
2. Replay the onboarding or setup guidance if it was dismissed.
3. Open `Today` if you need the next concrete action.
4. Open `Review` if you suspect a proposal is already waiting.
5. Open `Inbox` if the work only exists as a note so far.
6. Open the board only after the work is ready to be executed there.

Fallback rule:

- if you feel forced into `Queue`, `Ops`, or raw route knowledge for ordinary work, you are probably on the wrong surface

## Workflow 5: Continue Work After Review

Use this when a proposal has already been executed.

1. From `Review`, follow the board-aware link or go straight to the affected board.
2. Confirm the new or updated cards landed where you expected.
3. Move the work forward on the board.
4. Add comments, labels, due dates, or blockers as needed.
5. Capture any follow-up items back into `Inbox` instead of holding them in your head.

What success looks like:

- the board remains the visible place where work gets finished
- automation prepared the work, but did not replace the board

## Normal User Path vs Advanced Paths

Normal path:

- `Home`
- `Today`
- `Inbox`
- `Review`
- `Boards`

Advanced or operator paths:

- `Chat`
- `Activity`
- `Notifications`
- `Ops`
- `Access`
- `Archive`

Use the advanced paths when you specifically need diagnostics, collaboration evidence, or manual operator control.

## Managed-Key LLM Mode Notice

If your Taskdeck instance uses a platform-managed LLM provider key (rather than your own), fair-use limits and privacy disclosures apply to Chat and capture triage features. See `docs/security/MANAGED_KEY_USAGE_POLICY.md` for the full policy.
