# Manual Chapter 09: Troubleshooting

This chapter answers the most common first-run and daily-use questions.

## Why is `Home` not telling me much?

`Home` reflects the state of your workspace. If it feels thin:

1. start or replay setup
2. create a useful board
3. add one Inbox item
4. run the loop once

## Why is `Today` mostly empty?

That usually means:

- no proposals are waiting
- Inbox does not need triage
- no cards are overdue or due today

Open a board or capture new work in `Inbox`. An empty `Today` is not automatically an error.

## Why is `Inbox` empty?

Nothing is waiting to be shaped right now.

Recovery path:

1. capture a note from `Home`, `Today`, or a board
2. reopen it in `Inbox`
3. run `Start Triage`

## Why is `Review` empty?

No proposals need a decision yet.

If you expected one:

1. check whether the related Inbox item actually started triage
2. refresh `Review`
3. reopen the capture item and inspect its current status

## Why do I need review before apply?

Because review-first behavior is the trust model:

- proposals show what will change
- approval separates suggestion from action
- execution stays explicit

If the product asks for review, it is protecting the board workflow rather than slowing you down by accident.

## What does risk mean on a proposal?

Risk is a cue for how cautious the proposed change should feel.

Use it to decide:

- whether you want more context before approval
- whether rejection should include a reason
- whether to inspect the diff before execution

## Why do docs say project but the UI says board?

The shipped label is still `Boards`.
Some docs use `project` as the broader product-facing idea for the same workspace.

For now, read them as the same thing.

## Where are the advanced pages?

Some advanced pages:

- are hidden behind feature flags
- depend on role or operator context
- are intentionally secondary to the normal path

Check `Settings` if a page seems missing.

## How do I get a sample workspace?

From `frontend/taskdeck-web`:

```bash
npm run demo:seed
```

Use this when you want realistic examples instead of starting from empty state.

## Triage failed. What should I check?

Check:

1. whether the capture item is still in a triageable state
2. whether the current runtime or provider configuration is valid
3. whether the item already produced a proposal or failure state

If you still need to debug the system itself, that is the point where `Ops` becomes relevant.

## See Also

- [01_start_here.md](01_start_here.md)
- [04_inbox_and_review.md](04_inbox_and_review.md)
- [05_advanced_automation.md](05_advanced_automation.md)
