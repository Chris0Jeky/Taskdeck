/**
 * E2E: GH-1949 AC4 — the route-walking affordance smoke pass.
 *
 * WHAT THIS IS. A walk over the CLOSED inventory in
 * `tests/e2e/support/routeAffordanceInventory.ts`: for every row marked
 * `activate: true` it finds the control on its own route, activates it, and
 * asserts the consequence the inventory declares. It is not a crawler and it
 * discovers nothing — the sibling vitest guard
 * (`src/tests/guards/routeAffordanceCoverage.spec.ts`) is what keeps the
 * inventory in step with the real router table.
 *
 * WHAT IT PROVES. That each listed affordance is reachable and does the thing
 * the inventory says it does — a URL change, an HTTP call that returns 2xx, a
 * node appearing, or an attribute taking a value. It is deliberately shallow:
 * it does not assert the resulting content is correct.
 *
 * WHAT IT NEVER DOES. No irreversible action. Today's seal confirm is asserted
 * present and enabled and never clicked (the domain entity has `Seal()` and no
 * inverse). Execute is walked as far as the apply-to-board dialog and then
 * CANCELLED — which is why `support/applyConfirm.ts` is not used here: that
 * helper's contract is to accept the dialog. The boardless `accept-on-board`
 * control is asserted disabled (#1944) and never clicked. Openers are opened
 * and dismissed, never submitted.
 *
 * BUDGET. Four `test()` blocks. E2E Smoke runs every spec in `tests/e2e` on
 * chromium with `workers: 1`, a 45 s per-test timeout, a 12 min `globalTimeout`
 * and a 12 min step timeout, so this file's cost is charged to a shared
 * ceiling. Two blocks raise their own timeout to 90 s, the same relief
 * `review-proposals.spec.ts:45` takes for the triage chain. If the lane gets
 * tight, shed the metrics and integrations rows to slice 2 (mark them
 * `out-of-slice-1` in the inventory) rather than touching the workflow.
 *
 * RESPONSE WAITS. A `page.waitForResponse` armed just before a click can be
 * satisfied by a request the view issued on MOUNT, which would let a dead
 * control pass. Every walked `response` row is therefore covered one of two
 * ways, and which one holds is stated per row in the inventory:
 *
 *   - The row waits on a GET the view ALSO issues on mount — notifications
 *     (refresh, unread-only), calendar (previous, next), metrics (board select,
 *     range select). Each of those routes arms the mount read before
 *     `page.goto` and awaits it, so the row's own wait is registered only once
 *     the mount traffic is gone. The calendar and metrics rows additionally
 *     carry a `postCondition`; the notifications rows cannot (the filtered and
 *     unfiltered empty states render identical copy), and the metrics rows also
 *     pin the board id to the one the walk SELECTS, which is never the one the
 *     view auto-selects — that pin, not their `value` post-condition, is what
 *     rules out a dead select.
 *   - The row waits on a request no mount can raise — the POST rows (home quick
 *     capture, inbox archive, review approve: no view POSTs on mount) and
 *     `metrics.export-csv`, whose pattern is anchored to `/export`, which no
 *     mount read touches.
 *
 * A `response` row's wait is armed BEFORE the act and its rejection is parked
 * immediately (see `activate`), so a control that never becomes actionable
 * reports its own actionability failure rather than an unhandled rejection.
 * The mount reads those routes consume first are armed before `page.goto` for
 * the same reason and parked the same way (see `readOnMount`), so a `goto` that
 * throws reports its own failure instead of trailing a stray 30 s
 * `waitForResponse` rejection that lands on the worker with no test to own it.
 *
 * COMPLETENESS. Every block ends with `assertBlockCompleted`, which proves the
 * declared `WALK_PLAN` still names exactly the inventory's `activate: true` and
 * `guarded-not-activated` rows and that this block walked exactly its share. So
 * adding a row to the inventory without walking it is a red build rather than a
 * silent gap. See the comment on `WALK_PLAN` for why this is not an `afterAll`
 * tally.
 *
 * WHAT COMPLETENESS DOES NOT CATCH. Each block checks only its own share, so a
 * block that never runs — `test.skip`, a `grep` filter, `maxFailures` cutting
 * the run short — takes its rows out of the proof silently. The plan-versus-
 * inventory half still runs in every surviving block, so an unwalked NEW row is
 * always caught; it is a whole missing block that goes unnoticed. Reading the
 * run summary for four passing blocks is what closes that.
 */

import { expect, test, type Locator, type Page, type Response } from '@playwright/test'
import { registerAndAttachSession, type AuthResult } from './support/authSession'
import { createBoardWithColumn } from './support/boardHelpers'
import {
  createCaptureItem,
  triageCaptureItem,
  waitForProposalCreated,
} from './support/captureFlow'
import { assertOk } from './support/httpAsserts'
import {
  ROUTE_AFFORDANCE_INVENTORY,
  type AffordanceConsequence,
  type AffordanceSelector,
  type RouteAffordance,
} from './support/routeAffordanceInventory'

let auth: AuthResult

/** Ids the CURRENT test activated. Reset per test — see `WALK_PLAN`. */
const walkedIds = new Set<string>()
/** Ids of `guarded-not-activated` rows the current test asserted but declined. */
const guardedIds = new Set<string>()

const ALL_AFFORDANCES: RouteAffordance[] = ROUTE_AFFORDANCE_INVENTORY.flatMap(
  (entry) => entry.affordances,
)

const EXPECTED_ACTIVATE_IDS = ALL_AFFORDANCES
  .filter((affordance) => affordance.status.activate)
  .map((affordance) => affordance.id)

const EXPECTED_GUARDED_IDS = ALL_AFFORDANCES
  .filter((affordance) => !affordance.status.activate
    && affordance.status.reason === 'guarded-not-activated')
  .map((affordance) => affordance.id)

/**
 * Which block walks which rows.
 *
 * WHY A DECLARED PLAN AND NOT A RUNNING TALLY. The obvious design — accumulate
 * walked ids across the file and check the total in `afterAll` — is wrong here
 * on two counts. Playwright restarts the worker process after a failing test,
 * so the accumulator silently resets and reports every earlier row as unwalked;
 * and `fullyParallel: true` lets tests from one file land in different workers,
 * which splits the accumulator again. Both were observed on this file.
 *
 * So completeness is proved statically instead: this plan must name exactly the
 * inventory's `activate: true` and `guarded-not-activated` rows (checked on
 * every test), and each block must walk exactly the rows the plan gives it
 * (checked at the end of that block). Adding a row to the inventory without
 * walking it fails the first test that runs, in any worker, failure or not.
 */
const WALK_PLAN: Record<string, { activated: string[]; guarded: string[] }> = {
  'board-seeded': {
    activated: [
      'boards.new-board-toggle',
      'boards.create-submit',
      'boards.open-board-card',
      'board.settings-opener',
      'board.add-column-opener',
      'board.add-card-opener',
      'board.review',
      'review.clear-board-scope',
      'calendar.previous-month',
      'calendar.next-month',
      'metrics.board-select',
      'metrics.range-select',
      'metrics.export-csv',
      'integrations.add-connector-opener',
      'integrations.markdown-import-link',
      'views.new-view-toggle',
      'views.select-default-view',
      'views-detail.clear-filter',
      'notifications.refresh',
      'notifications.unread-only',
    ],
    guarded: [],
  },
  'home-and-inbox': {
    activated: [
      'home.quick-capture-submit',
      'inbox.ask-ai-boardless-opens-picker',
      'inbox.archive',
      'inbox.variant-toggle-nib',
    ],
    guarded: [],
  },
  today: {
    activated: ['today.write-note', 'today.seal-request'],
    guarded: ['today.seal-confirm'],
  },
  review: {
    activated: [
      'home.proposal-queue-card',
      'review.request-edit',
      'review.approve',
      'review.execute-opens-confirm',
      'review.queue-filter-pill',
    ],
    guarded: [],
  },
}

/** The plan and the inventory must name the same rows. Pure data; no browser. */
function assertPlanMatchesInventory(): void {
  const plannedActivations = Object.values(WALK_PLAN).flatMap((block) => block.activated)
  const duplicates = plannedActivations.filter((id, index) => plannedActivations.indexOf(id) !== index)
  expect(duplicates, `the walk plan lists these rows in more than one block: [${duplicates.join(', ')}]`)
    .toEqual([])

  expect(
    [...plannedActivations].sort(),
    'the walk plan must name exactly the inventory rows marked activate:true',
  ).toEqual([...EXPECTED_ACTIVATE_IDS].sort())

  expect(
    Object.values(WALK_PLAN).flatMap((block) => block.guarded).sort(),
    'the walk plan must name exactly the inventory rows marked guarded-not-activated',
  ).toEqual([...EXPECTED_GUARDED_IDS].sort())
}

/** Close a block: it walked its planned rows, and the plan covers the inventory. */
function assertBlockCompleted(block: keyof typeof WALK_PLAN): void {
  assertPlanMatchesInventory()

  const planned = WALK_PLAN[block]
  expect([...walkedIds].sort(), `the '${block}' block must activate exactly its planned rows`)
    .toEqual([...planned.activated].sort())
  expect([...guardedIds].sort(), `the '${block}' block must assert exactly its planned guarded rows`)
    .toEqual([...planned.guarded].sort())
}

interface WalkContext {
  boardId?: string
  boardName?: string
  /**
   * The board the metrics rows select. Deliberately NOT the board MetricsView
   * auto-selects on mount, so the mount read cannot satisfy those rows' waits.
   */
  metricsBoardId?: string
  /**
   * The month label the calendar route opened on, read from the page. The two
   * month rows declare their post-conditions against it: previous month must
   * move off it, next month must land back on it. Read rather than computed, so
   * the walk does not re-implement `CalendarView`'s own date formatting.
   */
  openingMonthLabel?: string
}

function affordanceById(id: string): RouteAffordance {
  const found = ALL_AFFORDANCES.find((affordance) => affordance.id === id)
  if (!found) {
    throw new Error(
      `the route-affordance inventory has no row '${id}'. The walk and the inventory `
        + 'must name the same rows; add the row or fix the id.',
    )
  }
  return found
}

/**
 * Substitutes the tokens the inventory uses in selectors and patterns.
 *
 * A token the context has not set yet THROWS rather than substituting an empty
 * string. An empty substitution turns a real assertion into a vacuous one — a
 * `textChangedFrom` post-condition against `''` passes for any non-empty label,
 * whether the control moved the month or not.
 */
function fillTokens(value: string, context: WalkContext): string {
  return value.replace(
    /\{(boardId|boardName|metricsBoardId|openingMonthLabel)\}/g,
    (_match, token: keyof WalkContext) => {
      const resolved = context[token]
      if (resolved === undefined || resolved === '') {
        throw new Error(
          `the walk substituted '{${token}}' before the context set it, which would assert `
            + `against an empty string. Set context.${token} before the row that uses it.`,
        )
      }
      return resolved
    },
  )
}

function locate(
  scope: Page | Locator,
  selector: AffordanceSelector,
  context: WalkContext,
): Locator {
  if (selector.kind === 'css') return scope.locator(selector.value)
  if (selector.kind === 'testId') return scope.getByTestId(selector.value)

  const name = selector.namePattern
    ? new RegExp(fillTokens(selector.name, context), selector.nameFlags)
    : fillTokens(selector.name, context)
  return scope.getByRole(selector.role as Parameters<Page['getByRole']>[0], {
    name,
    ...(selector.exact === undefined ? {} : { exact: selector.exact }),
  })
}

/**
 * Assert one consequence.
 *
 * `scope` is the SAME scope the affordance was found in, not the page. A row
 * driven inside one triage row or one board column must have its consequence
 * checked there too: with two columns or two captures on screen, a page-wide
 * `.first()` can be satisfied by a node the walk never opened.
 *
 * EXHAUSTIVE ON PURPOSE. This dispatch used to be an if-chain that fell through
 * to nothing, so a kind it did not know about asserted NOTHING while the row
 * still counted as walked (#2678). The switch's default assigns to `never`, and
 * this file is not type-checked by any gate (`tsconfig.app.json` and
 * `tsconfig.vitest.json` both cover `src/` only, and ESLint here is not
 * type-aware), so the throw is the half that actually runs. The type-checked
 * half is `CONSEQUENCE_ASSERTION_SITE` in
 * `src/tests/guards/routeAffordanceCoverage.spec.ts`, which fails
 * `npm run typecheck` when a kind is added to the union.
 */
async function expectConsequence(
  page: Page,
  scope: Page | Locator,
  id: string,
  consequence: AffordanceConsequence,
  context: WalkContext,
): Promise<void> {
  switch (consequence.kind) {
    case 'url':
      // Navigation is a property of the page, never of a scoped locator.
      await expect(page, `${id} must navigate to ${consequence.pathPattern}`)
        .toHaveURL(new RegExp(fillTokens(consequence.pathPattern, context)))
      return

    case 'node':
      await expect(
        scope.getByTestId(consequence.testId).first(),
        `${id} must render [data-testid="${consequence.testId}"] within its own scope`,
      ).toBeVisible()
      return

    case 'focus':
      await expect(
        scope.getByTestId(consequence.testId).first(),
        `${id} must leave [data-testid="${consequence.testId}"] holding focus`,
      ).toBeFocused()
      return

    case 'enabled': {
      const control = scope.locator(consequence.selector).first()
      await expect(control, `${id} must render ${consequence.selector}`).toBeVisible()
      await expect(control, `${id} must leave ${consequence.selector} enabled`).toBeEnabled()
      return
    }

    case 'value':
      await expect(
        scope.locator(consequence.selector).first(),
        `${id} must leave ${consequence.selector} holding the value ${consequence.value}`,
      ).toHaveValue(fillTokens(consequence.value, context))
      return

    case 'attribute':
      await expect(
        scope.locator(consequence.selector).first(),
        `${id} must leave ${consequence.selector} carrying ${consequence.attribute}="${consequence.value}"`,
      ).toHaveAttribute(consequence.attribute, consequence.value)
      return

    case 'text':
      await expect(
        scope.locator(consequence.selector).first(),
        `${id} must leave ${consequence.selector} reading '${consequence.text}'`,
      ).toHaveText(fillTokens(consequence.text, context))
      return

    case 'textChangedFrom':
      // THE ONLY NEGATED MATCHER IN THIS SWITCH, AND IT IS NOT VACUOUS ON A
      // MISSING NODE. `expect(locator).not.toHaveText(x)` FAILS when the
      // locator resolves to zero elements — it does not quietly pass the way
      // `not.toBeVisible()` does. Playwright special-cases only
      // visible/hidden/attached/detached/in-viewport and the array expressions
      // for a missing node; `to.have.text` falls through to "not satisfied
      // yet", polls to the timeout and reports `element(s) not found`.
      // Confirmed against Playwright 1.62.1; the code references and the
      // chromium probe are recorded on the `textChangedFrom` kind in
      // `support/routeAffordanceInventory.ts`. So this needs no `toHaveCount`
      // companion, and `calendar.previous-month` proves its own move rather
      // than leaning on `calendar.next-month` one row later.
      await expect(
        scope.locator(consequence.selector).first(),
        `${id} must move ${consequence.selector} off '${consequence.from}'`,
      ).not.toHaveText(fillTokens(consequence.from, context))
      return

    case 'response':
      // Only reachable through a misdeclared row: a response is asserted by the
      // wait `activate` arms BEFORE the act, so there is nothing to check here.
      // A guarded row declaring one is rejected by assertion 8 of
      // `src/tests/guards/routeAffordanceCoverage.spec.ts`.
      throw new Error(
        `${id} declares a 'response' consequence where only an element assertion can be made. `
          + 'A response is proved by the wait armed around an activation; a row that is never '
          + 'activated, or a postCondition, must declare an element consequence instead.',
      )

    default: {
      const unhandled: never = consequence
      throw new Error(
        `${id} declares an unhandled consequence kind: ${JSON.stringify(unhandled)}. `
          + 'Every kind in AffordanceConsequence must be asserted here and classified in '
          + 'CONSEQUENCE_ASSERTION_SITE (src/tests/guards/routeAffordanceCoverage.spec.ts); '
          + 'a kind this switch does not know would otherwise assert nothing at all.',
      )
    }
  }
}

/**
 * Activate one `activate: true` row and assert its declared consequence.
 *
 * `act` overrides the default click for controls that are driven differently
 * (a select, a checkbox, an input that submits on Enter). `scope` narrows the
 * search to one row or column when the same selector repeats on the page.
 */
async function activate(
  page: Page,
  id: string,
  context: WalkContext,
  options: { scope?: Page | Locator; act?: (target: Locator) => Promise<unknown> } = {},
): Promise<void> {
  const affordance = affordanceById(id)
  if (!affordance.status.activate) {
    throw new Error(
      `'${id}' is not an activate:true row (${affordance.status.reason}); the walk must not activate it`,
    )
  }

  const scope = options.scope ?? page
  const target = locate(scope, affordance.selector, context)
  await expect(target, `${id} (${affordance.source}) must be visible before activation`)
    .toBeVisible()

  const consequence = affordance.consequence
  if (consequence.kind === 'response') {
    const pattern = new RegExp(fillTokens(consequence.urlPattern, context))
    // Bounded deliberately. Left to the 90 s test timeout, a dead control burns
    // a minute and a half of the shared E2E Smoke budget before it reports; the
    // request this waits on is raised by the click that follows.
    const responsePromise = page.waitForResponse(
      (response) =>
        response.request().method() === consequence.method && pattern.test(response.url()),
      { timeout: 15_000 },
    )
    // Park the outcome NOW, before the act. If the act itself fails — a control
    // that never becomes actionable — nothing would await this promise, and its
    // 15 s rejection would surface as an unhandled rejection attributed to the
    // worker rather than the actionability failure that actually happened. The
    // handler attached here keeps the bound and lets the act's own error win.
    const settledResponse = responsePromise.then(
      (response) => ({ settled: 'fulfilled' as const, response }),
      (reason: unknown) => ({ settled: 'rejected' as const, reason }),
    )
    await (options.act ? options.act(target) : target.click())
    const outcome = await settledResponse
    if (outcome.settled === 'rejected') {
      throw outcome.reason instanceof Error ? outcome.reason : new Error(String(outcome.reason))
    }
    await assertOk(outcome.response, `${id} (${affordance.source})`)
  } else {
    await (options.act ? options.act(target) : target.click())
    await expectConsequence(page, scope, id, consequence, context)
  }

  // The independent half, where the row has one. A network wait cannot tell a
  // live control from a dead one on a view that already fetched; this can.
  if (affordance.postCondition) {
    await expectConsequence(page, scope, id, affordance.postCondition, context)
  }

  walkedIds.add(id)
}

/**
 * Assert a `guarded-not-activated` row WITHOUT clicking it. The row's declared
 * consequence carries what must hold, and `assertEnabled` adds the check that
 * survives whatever that consequence happens to be: the located control itself
 * must be a live, reachable choice the walk declines, not merely a panel that
 * rendered. Losing that check is how a guarded row whose consequence names some
 * OTHER node would end up proving nothing about the control it guards (#2678).
 */
async function assertReachableButNotActivated(
  page: Page,
  id: string,
  context: WalkContext,
): Promise<void> {
  const affordance = affordanceById(id)
  if (affordance.status.activate || affordance.status.reason !== 'guarded-not-activated') {
    throw new Error(`'${id}' is not a guarded-not-activated row`)
  }

  const target = locate(page, affordance.selector, context)
  await expect(target, `${id} (${affordance.source}) must be present`).toBeVisible()
  await expectConsequence(page, page, id, affordance.consequence, context)
  // THE TYPE IS THE ENFORCEMENT; THIS READ CANNOT TAKE ITS FALSE BRANCH.
  // `AffordanceStatus`'s guarded variant declares `assertEnabled: true` as a
  // REQUIRED LITERAL, so no row reaching here can have it false or absent and
  // the `else` is unreachable at runtime. It is kept as belt-and-braces: it
  // documents at the point of use that the enabled check is conditional on the
  // flag, and it is what starts working if the field is ever widened to
  // `boolean`. Read it as "the type already guaranteed this", not as a choice
  // rows make. The guard's matching read (assertion 8 in
  // `src/tests/guards/routeAffordanceCoverage.spec.ts`) is dead for the same
  // reason and says so there.
  if (affordance.status.assertEnabled) {
    await expect(
      target,
      `${id} (${affordance.source}) must be enabled: a declined choice has to be a live one`,
    ).toBeEnabled()
  }
  guardedIds.add(id)
}

/**
 * Navigate to a route and consume the read it issues on MOUNT.
 *
 * WHY THE MOUNT READ IS CONSUMED AT ALL. Four routes here (calendar, metrics,
 * notifications, home) fetch on mount, and three of them own `response` rows
 * waiting on the SAME endpoint. A wait armed after navigation could be settled
 * by the mount's own request, so a dead control would still pass. Awaiting the
 * mount read here takes it off the wire before any row arms its own wait.
 *
 * WHY THE OUTCOME IS PARKED. The wait must be armed BEFORE `page.goto` — that
 * is the whole point, the response can arrive during the navigation — but a
 * `goto` that throws would then leave the `waitForResponse` promise with
 * nothing awaiting it, and its 30 s default rejection would surface later as an
 * unhandled rejection attributed to the worker rather than as the navigation
 * failure that actually happened. Parking the outcome the moment it is armed,
 * exactly as `activate` does for row waits, keeps the navigation's own error
 * first while still failing on a mount read that never arrives. The success
 * path is unchanged: armed before, awaited after, asserted 2xx.
 */
async function readOnMount(
  page: Page,
  label: string,
  matches: (response: Response) => boolean,
  navigate: () => Promise<unknown>,
): Promise<void> {
  const settledRead = page.waitForResponse(matches).then(
    (response) => ({ settled: 'fulfilled' as const, response }),
    (reason: unknown) => ({ settled: 'rejected' as const, reason }),
  )
  await navigate()
  const outcome = await settledRead
  if (outcome.settled === 'rejected') {
    throw outcome.reason instanceof Error ? outcome.reason : new Error(String(outcome.reason))
  }
  await assertOk(outcome.response, label)
}

function uniqueSeed(): string {
  return `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
}

test.beforeEach(async ({ page, request }) => {
  walkedIds.clear()
  guardedIds.clear()
  auth = await registerAndAttachSession(page, request, 'route-affordances')
})

// ─────────────────────────────────────────────────────────────────────────────

test('walks the board-seeded route affordances', async ({ page, request }) => {
  // Eight routes in one block: the seeded board is the expensive part and every
  // one of them needs it, so re-seeding per route would cost more than it saves.
  test.setTimeout(90_000)

  const seed = uniqueSeed()
  const boardName = `Route Walk ${seed}`
  const boardId = await createBoardWithColumn(request, auth, seed, {
    boardNamePrefix: 'Route Walk',
    description: 'route-affordance smoke walk',
    columnNamePrefix: 'Backlog',
  })
  const context: WalkContext = { boardId, boardName }

  // ── /workspace/boards ────────────────────────────────────────────────────
  await page.goto('/workspace/boards')
  await expect(page.getByRole('button', { name: `Open board: ${boardName}` })).toBeVisible()

  await activate(page, 'boards.new-board-toggle', context)
  await page.getByPlaceholder('Board name').fill(`Route Walk Created ${seed}`)
  await activate(page, 'boards.create-submit', context)
  const createdBoardId = new URL(page.url()).pathname.split('/').pop() as string
  expect(createdBoardId).toBeTruthy()

  await page.goto('/workspace/boards')
  await activate(page, 'boards.open-board-card', context)

  // ── /workspace/boards/:id — openers only, nothing submitted ──────────────
  await activate(page, 'board.settings-opener', context)
  await page.getByTestId('paper-board-dialog-cancel').click()
  await expect(page.getByTestId('paper-board-dialog-name')).toBeHidden()

  await activate(page, 'board.add-column-opener', context)
  await page.getByTestId('paper-board-add-column-cancel').click()
  await expect(page.getByTestId('paper-board-add-column-form')).toBeHidden()

  await activate(page, 'board.add-card-opener', context, {
    scope: page.locator('[data-column-id]').first(),
  })
  await page.getByTestId('paper-card-composer-cancel').click()
  await expect(page.getByTestId('paper-card-composer')).toBeHidden()

  await activate(page, 'board.review', context)

  // ── /workspace/review?boardId= — the scope chip lives on the empty state ─
  // `paper-review-clear-scope` renders in the board-scoped EMPTY branch of the
  // deep pane, so it is walked here, on a board with no proposals, rather than
  // in the review block below, whose board deliberately has one.
  await expect(page.getByTestId('paper-review-view')).toBeVisible()
  await activate(page, 'review.clear-board-scope', context)

  // ── /workspace/calendar ──────────────────────────────────────────────────
  // CalendarView fetches in onMounted and again on every viewDate change
  // (CalendarView.vue:178-179), so a wait armed after navigation could be
  // settled by the mount read and a dead month button would still pass. Same
  // treatment as metrics and notifications: consume the mount read first. The
  // month label is then the independent half, declared as a postCondition on
  // both rows rather than asserted ad hoc here.
  await readOnMount(
    page,
    'calendar read issued on mount',
    (response) =>
      response.request().method() === 'GET' && /\/api\/workspace\/calendar/.test(response.url()),
    () => page.goto('/workspace/calendar'),
  )

  const monthLabel = page.locator('.paper-calendar__month-label')
  await expect(monthLabel).toBeVisible()
  const openingMonth = (await monthLabel.textContent())?.trim()
  expect(openingMonth, 'the calendar must render a month label to walk against').toBeTruthy()
  context.openingMonthLabel = openingMonth

  await activate(page, 'calendar.previous-month', context)
  await activate(page, 'calendar.next-month', context)
  // `calendar.timeline-mode` is out of slice 1: the empty-state branch at
  // CalendarView.vue:260 wins over both view modes while totalCards is 0.

  // ── /workspace/metrics ───────────────────────────────────────────────────
  // MetricsView auto-selects boards[0] in onMounted and its watcher fetches in
  // the same tick, so a wait armed after navigation could be satisfied by that
  // mount read and a dead select would still pass. Two defences: consume the
  // mount read here, and pin the rows to the OTHER of the two boards this block
  // seeded, so the mount's URL can never match them.
  await readOnMount(
    page,
    'metrics read issued on mount',
    (response) =>
      response.request().method() === 'GET'
      && /\/api\/metrics\/boards\/[a-f0-9-]+\?from=/.test(response.url()),
    () => page.goto('/workspace/metrics'),
  )

  const boardSelect = page.locator('#board-select')
  await expect(boardSelect, 'the board select must settle on its auto-selection first')
    .not.toHaveValue('')
  const preselected = await boardSelect.inputValue()
  context.metricsBoardId = preselected === boardId ? createdBoardId : boardId
  expect(
    context.metricsBoardId,
    'the metrics rows must target a board the mount did not already read',
  ).not.toBe(preselected)

  await activate(page, 'metrics.board-select', context, {
    act: (target) => target.selectOption(context.metricsBoardId as string),
  })
  await activate(page, 'metrics.range-select', context, {
    act: (target) => target.selectOption({ label: 'Last 90 days' }),
  })
  await activate(page, 'metrics.export-csv', context)

  // ── /workspace/integrations ──────────────────────────────────────────────
  await page.goto('/workspace/integrations')
  await activate(page, 'integrations.add-connector-opener', context)
  await activate(page, 'integrations.markdown-import-link', context)

  // ── /workspace/views and /workspace/views/:viewId ────────────────────────
  await page.goto('/workspace/views')
  await activate(page, 'views.new-view-toggle', context)
  await activate(page, 'views.select-default-view', context)
  await activate(page, 'views-detail.clear-filter', context)

  // ── /workspace/notifications ─────────────────────────────────────────────
  // The view fetches on mount and the Refresh button renders on first paint, so
  // a wait armed after navigation could be satisfied by the mount read. Consume
  // it first: only a genuinely new request can settle the rows below. Neither
  // row can carry a post-condition, because the filtered and unfiltered empty
  // states render identical copy for a user with no notifications.
  await readOnMount(
    page,
    'notifications read issued on mount',
    (response) =>
      response.request().method() === 'GET' && /\/api\/notifications(\?|$)/.test(response.url()),
    () => page.goto('/workspace/notifications'),
  )

  await expect(page.getByRole('button', { name: 'Refresh' })).toBeVisible()
  await activate(page, 'notifications.refresh', context)
  await activate(page, 'notifications.unread-only', context, {
    act: (target) => target.check(),
  })

  assertBlockCompleted('board-seeded')
})

test('walks the Home quick capture and the Inbox triage affordances', async ({ page, request }) => {
  const seed = uniqueSeed()
  // One board exists so the boardless picker reports "no board chosen" rather
  // than "no boards at all"; both disable the confirm, but only the first is
  // the #1944 shape this row is here to pin.
  await createBoardWithColumn(request, auth, seed, {
    boardNamePrefix: 'Route Walk Inbox',
    description: 'route-affordance boardless capture walk',
    columnNamePrefix: 'Backlog',
  })
  const context: WalkContext = {}
  const captureText = `Route walk boardless capture ${seed}`

  // ── /workspace/home — await the summary read rather than sleeping ────────
  await readOnMount(
    page,
    'Home summary read on first paint',
    (response) =>
      response.request().method() === 'GET' && /\/api\/workspace\/home$/.test(response.url()),
    () => page.goto('/workspace/home'),
  )
  await expect(page.getByTestId('paper-home')).toBeVisible()

  // The Home quick capture posts `boardId: null`, so it seeds the boardless
  // Inbox row the next two assertions need.
  await activate(page, 'home.quick-capture-submit', context, {
    act: async (target) => {
      await target.fill(captureText)
      await target.press('Enter')
    },
  })

  // ── /workspace/inbox ─────────────────────────────────────────────────────
  await page.goto('/workspace/inbox')
  const captureRow = page.locator('.paper-triage__row').filter({ hasText: captureText }).first()
  await expect(captureRow).toBeVisible()

  await activate(page, 'inbox.ask-ai-boardless-opens-picker', context, { scope: captureRow })
  await expect(
    captureRow.getByTestId('capture-board-pick'),
    'the board picker must be open around the disabled confirm',
  ).toBeVisible()
  await captureRow.locator('[data-action="cancel-board-pick"]').click()
  await expect(captureRow.getByTestId('capture-board-pick')).toBeHidden()

  await activate(page, 'inbox.archive', context, { scope: captureRow })

  await activate(page, 'inbox.variant-toggle-nib', context)

  assertBlockCompleted('home-and-inbox')
})

test('walks the Today cover without activating the irreversible seal', async ({ page }) => {
  const context: WalkContext = {}

  await page.goto('/workspace/today')
  await expect(page.locator('[data-action="seal"]')).toBeVisible()

  await activate(page, 'today.write-note', context)
  await expect(
    page.getByTestId('line-for-tomorrow-input'),
    'Write a note must move the caret to the line-for-tomorrow field, not raise a toast',
  ).toBeFocused()

  await activate(page, 'today.seal-request', context)
  await assertReachableButNotActivated(page, 'today.seal-confirm', context)

  // Back out. Sealing is terminal, so the walk leaves the day unsealed.
  await page.locator('[data-action="seal-cancel"]').click()
  await expect(page.getByTestId('seal-confirm')).toBeHidden()

  assertBlockCompleted('today')
})

test('walks the Review decision rail and the Home proposal card', async ({ page, request }) => {
  // The triage chain (create board, capture, triage, poll for the proposal) is
  // the slow part; the deterministic Mock LLM keeps it reproducible.
  test.setTimeout(90_000)

  const seed = uniqueSeed()
  const boardId = await createBoardWithColumn(request, auth, seed, {
    boardNamePrefix: 'Route Walk Review',
    description: 'route-affordance review decision walk',
    columnNamePrefix: 'Backlog',
  })
  const context: WalkContext = { boardId }

  const cardTitle = `Route walk review card ${seed}`
  const capture = await createCaptureItem(request, auth, boardId, `- [ ] ${cardTitle}`)
  await triageCaptureItem(request, auth, capture.id)
  const triaged = await waitForProposalCreated(request, auth, capture.id)
  expect(triaged.provenance?.proposalId).toBeTruthy()

  // ── /workspace/home — the proposal card is the queue's proposal row ──────
  await page.goto('/workspace/home')
  await expect(page.getByTestId('paper-home-card-proposal').first()).toBeVisible({ timeout: 15_000 })
  await activate(page, 'home.proposal-queue-card', context)

  // ── /workspace/review ────────────────────────────────────────────────────
  await expect(page.getByTestId('paper-review-view')).toBeVisible()
  await expect(
    page.getByRole('heading', { level: 1, name: `Capture triage: ${cardTitle}` }),
  ).toBeVisible({ timeout: 15_000 })

  await activate(page, 'review.request-edit', context)
  await page.getByTestId('revision-cancel').click()
  await expect(page.getByTestId('revision-editor')).toBeHidden()

  // Phase 1 of the ADR-0003 two-phase apply.
  await activate(page, 'review.approve', context)

  // Phase 2 stops at the confirmation. `apply-confirm-accept` is asserted
  // enabled — the gate must be a real choice — and then the dialog is
  // cancelled. Nothing is written to the board.
  await activate(page, 'review.execute-opens-confirm', context)
  await expect(page.getByTestId('apply-confirm-accept')).toBeEnabled()
  await page.getByTestId('apply-confirm-cancel').click()
  await expect(page.getByTestId('apply-confirm-dialog')).toBeHidden()

  await activate(page, 'review.queue-filter-pill', context)

  assertBlockCompleted('review')
})
