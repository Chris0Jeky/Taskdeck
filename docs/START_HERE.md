# Start Here

Taskdeck is a local-first execution workspace built around one simple loop:

1. capture something quickly
2. triage it into a proposal
3. review the proposal
4. apply it explicitly
5. work the resulting board/cards

If you only remember one thing, remember this:
Taskdeck is strongest when capture is cheap and board mutation stays review-first.

## What To Know Up Front

- `Boards` are where work is done.
- `Inbox` is where messy input belongs first.
- `Automations -> Proposals` is the main review surface.
- `Queue` and `Ops` are advanced/operator surfaces, not the normal first-run path.
- The current shipped shell still starts from `Boards`; planned `Home` and `Today` surfaces are not shipped yet.

## First 15 Minutes

### Option A: use the current product loop

1. Start the backend and frontend.
2. Register or log in.
3. Create a board from `Boards`.
4. Optionally apply a starter pack from board settings.
5. Capture a note, task, or idea into `Inbox`.
6. Start triage on that Inbox item.
7. Open `Automations -> Proposals`.
8. Review, approve, and execute the proposal.
9. Return to the board and work the resulting cards.

### Option B: use the seeded demo workspace

From `frontend/taskdeck-web`:

```bash
npm run demo:seed
```

Then:

1. sign in
2. open the seeded boards
3. inspect `Inbox`
4. open `Automations -> Proposals`
5. execute a proposal and return to a board

For the full demo/operator workflow, use [DEMO_PLAYBOOK.md](/C:/Users/jekyt/source/Taskdeck/docs/DEMO_PLAYBOOK.md).

## Page Map

Core surfaces:
- `Boards`
- `Board view`
- `Inbox`
- `Automations -> Proposals`
- starter packs

Supporting trust surfaces:
- `Notifications`
- `Activity`
- comments and mentions

Advanced/operator surfaces:
- `Queue`
- `Chat`
- `Ops`
- `Access`
- `Archive`

## When To Use What

Use `Inbox` when:
- the input is messy
- you do not want to structure it yet
- you want Taskdeck to suggest the board update later

Use `Automations -> Proposals` when:
- you want to review what Taskdeck is about to change
- you need the trust boundary before board mutation

Use `Boards` when:
- you are executing work
- you need to move cards, add details, or collaborate through comments

Use `Queue` only when:
- you already know the explicit instruction flow
- you are doing a power-user or debugging task

## Read Next

- [USER_MANUAL.md](/C:/Users/jekyt/source/Taskdeck/docs/USER_MANUAL.md): full current usage reference
- [DOGFOODING_GUIDE.md](/C:/Users/jekyt/source/Taskdeck/docs/DOGFOODING_GUIDE.md): daily-use cadence and friction logging
- [DEMO_PLAYBOOK.md](/C:/Users/jekyt/source/Taskdeck/docs/DEMO_PLAYBOOK.md): seeded demo and walkthrough flow
- [SCENARIOS.md](/C:/Users/jekyt/source/Taskdeck/docs/SCENARIOS.md): JSON scenario runner
- [STATUS.md](/C:/Users/jekyt/source/Taskdeck/docs/STATUS.md): current shipped reality
