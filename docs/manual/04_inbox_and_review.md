# Manual Chapter 04: Inbox And Review

This chapter explains the capture and review loop.

## Inbox

### When should I use this page?

Use `Inbox` when:

- the input is rough
- you want to capture it before you forget it
- you are not ready to create or edit board work directly

Typical examples:

- a bug note
- pasted meeting output
- a transcript
- a follow-up idea
- a rough checklist or plan

### What happens here?

The normal sequence is:

1. capture an item
2. open it in `Inbox`
3. run `Start Triage`
4. let Taskdeck prepare a proposal-ready change
5. open the linked proposal in `Review`

### If this page is empty

If `Inbox` is empty:

- nothing is waiting to be shaped right now
- start from `Home` or `Today` and add a fresh note
- if you want examples immediately, seed the demo workspace

### Common mistakes

- using `Queue` instead of `Inbox` for normal messy intake
- assuming triage directly edits a board without passing through `Review`
- leaving important context in local notes instead of capturing it here

## Review

### When should I use this page?

Use `Review` when:

- you need the trust boundary before a board changes
- you want to inspect summary, impact, risk, provenance, and affected entities
- you need to approve, reject, or execute explicitly

### What should I look at first?

For each proposal, check:

1. plain-language summary
2. planned changes
3. risk cue
4. source and provenance
5. affected entities
6. board deep links if you want more context

### What do the main actions mean?

`Approve`
- accept the proposal and make it executable

`Reject`
- stop the proposal and optionally explain why

`Execute`
- apply an already approved proposal to the board

`View Diff`
- inspect the detailed change payload

### If this page is empty

If `Review` is empty:

- there may be no proposals waiting for a decision
- `Inbox` may not have produced a proposal yet
- you may simply be clear right now

The normal recovery path is to go back to `Inbox` and run triage on a new or failed capture.

## Risk, Provenance, And Trust

`Review` is where Taskdeck makes automation legible:

- provenance shows where the request came from
- risk cues show how cautious the proposed change should feel
- review links and board links help you keep context

If the product asks you for approval, that is working as intended.

## Common mistakes

- thinking `Approve` and `Execute` are the same step
- skipping provenance and affected entities when the change looks simple
- expecting `Review` to be only for power users
- assuming a board should change before a proposal exists

## See Also

- [02_home_and_today.md](02_home_and_today.md)
- [03_projects_and_cards.md](03_projects_and_cards.md)
- [09_troubleshooting.md](09_troubleshooting.md)
