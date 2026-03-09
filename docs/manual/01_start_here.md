# Manual Chapter 01: Start Here

This chapter explains what Taskdeck is for and how to get to first value quickly.

## What Taskdeck Is For

Taskdeck helps you move from messy input to explicit, reviewable work.

The core promise is simple:

1. capture something fast
2. shape it into a proposal
3. review the change before it lands
4. continue the real work on a board

Taskdeck is not trying to hide automation behind silent mutation. The review step is part of the product, not a temporary limitation.

## The Fastest Useful Path

1. open `Home`
2. create or open a useful board
3. put one note or task into `Inbox`
4. run `Start Triage`
5. open `Review`
6. approve and execute one proposal
7. continue from the board

If you only do one thing on your first run, do that loop once.

## Glossary

`Home`
- the default landing page and reset surface

`Today`
- the agenda view for review, triage, and due or blocked board work

`Inbox`
- where rough notes, pasted text, follow-ups, and transcripts wait to be shaped

`Review`
- the trust gate where proposals stop before they touch a board

`Boards`
- the shipped label for workspaces where cards live

`Projects`
- the product-facing term that may appear in docs or later UI language for the same board concept

`Queue`
- an advanced manual automation input surface, not the normal first-run path

`Agents`
- future-facing supervised assistant surfaces that are not part of the shipped shell yet

## What Is Shipped Today

Shipped and part of the normal path:

- `Home`
- `Today`
- `Inbox`
- `Review`
- `Boards`

Shipped but usually advanced:

- `Chat`
- `Activity`
- `Notifications`
- `Ops`
- `Access`
- `Archive`

Planned, not shipped:

- `Agents`
- `Runs`
- `Knowledge`
- `Integrations`

## If You Want A Richer First Run

From `frontend/taskdeck-web`:

```bash
npm run demo:seed
```

Use the seeded workspace when:

- you want real examples immediately
- you are evaluating the product rather than starting empty
- you need a walkthrough or training baseline

## Common Mistakes

- starting from `Queue` instead of `Inbox`
- thinking `Review` is optional
- expecting `Agents` or `Integrations` to be current top-level workspace pages
- reading `Projects` in docs as a different concept from the current `Boards` label

## See Also

- [02_home_and_today.md](02_home_and_today.md)
- [04_inbox_and_review.md](04_inbox_and_review.md)
- [09_troubleshooting.md](09_troubleshooting.md)
