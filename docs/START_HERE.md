# Start Here

This is the first doc to read if you are new to Taskdeck.

Taskdeck is a review-first execution workspace. The normal path is:

1. start from `Home`
2. decide what matters in `Today`
3. capture rough input in `Inbox`
4. inspect proposed changes in `Review`
5. execute explicitly
6. continue the real work on a board

## What Is Shipped Right Now

The novice-first shell is now real:

- `Home` is the default landing surface.
- `Today` shows the daily agenda across review, capture triage, and board work.
- `Inbox` is the low-friction intake surface.
- `Review` is the normal automation surface and trust gate.
- `Boards` is still the shipped label for project workspaces.

Advanced or operator-facing surfaces also exist, but they are not the normal starting path:

- `Chat`
- `Activity`
- `Ops`
- `Access`
- `Archive`

Planned but not shipped yet:

- `Agents`
- `Runs`
- `Knowledge`
- `Integrations`

## Two-Minute First Value Path

If you want the shortest real workflow:

1. open `Home`
2. create or resume a useful board from the setup loop
3. drop one note, bug, or transcript into `Inbox`
4. run `Start Triage`
5. open `Review`
6. approve and execute one proposal
7. open the board and continue from the resulting card or board change

## Pick The Right Surface

Use `Home` when:

- you do not want to guess where to start
- you want the setup loop
- you want recent-board context and a recommended next action

Use `Today` when:

- you want the daily agenda
- you need one place to see review, triage, overdue, due-today, and blocked work

Use `Inbox` when:

- the input is messy
- you want to save it now and shape it later
- you do not want to jump straight into board edits

Use `Review` when:

- you want to inspect changes before they touch a board
- you need the proposal summary, risk, provenance, and approve-or-reject step

Use `Boards` when:

- the work is already clear enough to live on a board
- you need cards, comments, due dates, labels, or board-specific actions

## Common First-Run Questions

Why does the doc talk about projects when the UI says boards?

- Product language is moving toward "projects" for normal users, but the shipped route label is still `Boards`. Treat them as the same workspace today.

Why do I need `Review` before a board changes?

- That is the trust model. Taskdeck proposes changes first and waits for an explicit decision before it writes to a board.

What if every page looks empty?

- Start the setup loop from `Home`, create one useful board, then add one Inbox item and run the loop once. If you want a fuller workspace immediately, seed the demo workspace with `npm run demo:seed`.

Do I need `Queue` or `Ops`?

- No for normal first-run use. Stay in `Home`, `Today`, `Inbox`, `Review`, and `Boards` unless you are doing operator or debugging work.

## Local Start

Backend:

```bash
dotnet restore backend/Taskdeck.sln
dotnet run --project backend/src/Taskdeck.Api/Taskdeck.Api.csproj
```

Frontend:

```bash
cd frontend/taskdeck-web
npm install
npm run dev
```

Default URLs:

- API: `http://localhost:5000`
- Swagger: `http://localhost:5000/swagger`
- Frontend: `http://localhost:5173`

## Want A Richer First Run?

Seed a believable workspace so `Home`, `Today`, `Inbox`, and `Review` are already populated:

```bash
cd frontend/taskdeck-web
npm run demo:seed
```

Use this when:

- you are evaluating the product
- you want realistic examples instead of empty states
- you need a walkthrough state for demos or training

## Next Docs

- [USER_MANUAL.md](USER_MANUAL.md) for the manual index and chapter map
- [manual/01_start_here.md](manual/01_start_here.md) for the product mental model and glossary
- [manual/02_home_and_today.md](manual/02_home_and_today.md) for the day-to-day shell
- [manual/04_inbox_and_review.md](manual/04_inbox_and_review.md) for the capture and review loop
- [manual/09_troubleshooting.md](manual/09_troubleshooting.md) for common confusion points and empty-state recovery
- [product/DEMO_PLAYBOOK.md](product/DEMO_PLAYBOOK.md) for seeded demos and stakeholder walkthroughs
