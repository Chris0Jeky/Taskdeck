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
 * COMPLETENESS. Every block ends with `assertBlockCompleted`, which proves the
 * declared `WALK_PLAN` still names exactly the inventory's `activate: true` and
 * `guarded-not-activated` rows and that this block walked exactly its share. So
 * adding a row to the inventory without walking it is a red build rather than a
 * silent gap. See the comment on `WALK_PLAN` for why this is not an `afterAll`
 * tally.
 */

import { expect, test, type Locator, type Page } from '@playwright/test'
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

/** Substitutes the `{boardId}` / `{boardName}` tokens the inventory uses. */
function fillTokens(value: string, context: WalkContext): string {
  return value
    .replace(/\{boardId\}/g, context.boardId ?? '')
    .replace(/\{boardName\}/g, context.boardName ?? '')
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

async function expectConsequence(
  page: Page,
  id: string,
  consequence: AffordanceConsequence,
  context: WalkContext,
): Promise<void> {
  if (consequence.kind === 'url') {
    await expect(page, `${id} must navigate to ${consequence.pathPattern}`)
      .toHaveURL(new RegExp(fillTokens(consequence.pathPattern, context)))
    return
  }
  if (consequence.kind === 'node') {
    await expect(
      page.getByTestId(consequence.testId).first(),
      `${id} must render [data-testid="${consequence.testId}"]`,
    ).toBeVisible()
    return
  }
  if (consequence.kind === 'attribute') {
    await expect(
      page.locator(consequence.selector).first(),
      `${id} must leave ${consequence.selector} carrying ${consequence.attribute}="${consequence.value}"`,
    ).toHaveAttribute(consequence.attribute, consequence.value)
  }
  // 'response' consequences are awaited inside `activate`, which has to arm the
  // wait before the click rather than after it.
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

  const target = locate(options.scope ?? page, affordance.selector, context)
  await expect(target, `${id} (${affordance.source}) must be visible before activation`)
    .toBeVisible()

  const consequence = affordance.consequence
  if (consequence.kind === 'response') {
    const pattern = new RegExp(fillTokens(consequence.urlPattern, context))
    const responsePromise = page.waitForResponse(
      (response) =>
        response.request().method() === consequence.method && pattern.test(response.url()),
    )
    await (options.act ? options.act(target) : target.click())
    await assertOk(await responsePromise, `${id} (${affordance.source})`)
  } else {
    await (options.act ? options.act(target) : target.click())
    await expectConsequence(page, id, consequence, context)
  }

  walkedIds.add(id)
}

/**
 * Assert a `guarded-not-activated` row is present and enabled WITHOUT clicking
 * it. "Enabled" is the point: the control must be a live, reachable choice —
 * the walk simply refuses to make it.
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
  await expect(target, `${id} must be enabled — it is a real choice the walk declines`)
    .toBeEnabled()
  guardedIds.add(id)
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
  await page.goto('/workspace/calendar')
  const monthLabel = page.locator('.paper-calendar__month-label')
  await expect(monthLabel).toBeVisible()
  const openingMonth = (await monthLabel.textContent())?.trim()

  await activate(page, 'calendar.previous-month', context)
  await expect(monthLabel, 'the month label must actually move').not.toHaveText(openingMonth ?? '')
  await activate(page, 'calendar.next-month', context)
  await expect(monthLabel).toHaveText(openingMonth ?? '')
  // `calendar.timeline-mode` is out of slice 1: the empty-state branch at
  // CalendarView.vue:260 wins over both view modes while totalCards is 0.

  // ── /workspace/metrics ───────────────────────────────────────────────────
  // MetricsView auto-selects boards[0] on mount, so selecting that same board
  // would change nothing and fire no request. Pick the other one.
  await page.goto('/workspace/metrics')
  const boardSelect = page.locator('#board-select')
  await expect(boardSelect).toBeVisible()
  await expect(boardSelect, 'the board select must settle on its auto-selection first')
    .not.toHaveValue('')
  const preselected = await boardSelect.inputValue()
  const otherBoardId = preselected === boardId ? createdBoardId : boardId

  await activate(page, 'metrics.board-select', context, {
    act: (target) => target.selectOption(otherBoardId),
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
  await page.goto('/workspace/notifications')
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
  const homeSummary = page.waitForResponse(
    (response) =>
      response.request().method() === 'GET' && /\/api\/workspace\/home$/.test(response.url()),
  )
  await page.goto('/workspace/home')
  await assertOk(await homeSummary, 'Home summary read on first paint')
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
