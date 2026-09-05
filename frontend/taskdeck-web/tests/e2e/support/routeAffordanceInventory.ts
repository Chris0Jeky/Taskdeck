/**
 * GH-1949 AC4 — the CLOSED route-affordance inventory.
 *
 * WHAT THIS IS. A hand-authored list of the affordances the route-walking smoke
 * pass knows about, one entry per walked `/workspace` route. It is data only:
 * `tests/e2e/route-affordances.spec.ts` walks it in a browser, and
 * `src/tests/guards/routeAffordanceCoverage.spec.ts` proves it stays in step
 * with the real router table. It is NOT a crawler and makes no claim about any
 * affordance it does not list — adding one is the whole point of the file.
 *
 * THIS MODULE MUST IMPORT NOTHING. The vitest coverage guard consumes it under
 * `tsconfig.vitest.json`, whose `types` are `["vite/client",
 * "vite-plugin-pwa/client"]` with no `"node"`. A `@playwright/test`, `node:*`
 * or `../../src` import here would either break that type-check or drag browser
 * fixtures into a unit test. Selectors are therefore described as data and
 * resolved into Playwright locators by the spec.
 *
 * SHELL AFFORDANCES ARE NOT ROWS. The Paper top bar (bell, gear, avatar) and
 * the sidebar are chrome shared by every route, not per-route affordances.
 * Listing them 12 times would say nothing new. Their targets are already pinned
 * against the real router table by
 * `src/tests/router/workspaceRouteStability.spec.ts` (the `#1932` named-target
 * describe block), and the sidebar link is exercised in
 * `tests/e2e/review-proposals.spec.ts:113`.
 *
 * KEYSTROKE AFFORDANCES ARE NOT ROWS EITHER. Shortcut truth is owned by
 * `src/tests/guards/shortcutLedgerTruth.spec.ts` and
 * `src/tests/guards/shortcutNotation.spec.ts`. This inventory covers pointer
 * affordances only; the one keyboard path it does use (Enter submitting the
 * Home quick-capture form) is incidental to that control's own submit, not a
 * shortcut claim.
 *
 * DESTRUCTIVE-ACTION POLICY. Rows carry an explicit `status`. Only
 * `activate: true` rows are clicked. `guarded-not-activated` rows are asserted
 * present and enabled and never clicked — Today's seal confirm is irreversible
 * by design (`src/views/paper/PaperTodayView.vue:34-37`: the domain entity has
 * `Seal()` and no inverse). Execute is walked as far as the confirmation dialog
 * and then cancelled; `apply-confirm-accept` is never clicked. The disabled
 * boardless `accept-on-board` control is asserted disabled, never clicked.
 * Openers (board settings, add column, add card, add connector, new view, new
 * board form) are opened and never submitted, except `boards.create-submit`,
 * which runs against the per-test throwaway user.
 *
 * REVIEW ROWS OUT OF SCOPE. `decision-reject` and `decision-defer` are not
 * listed: the five-row cap is already spent on the approve/execute pair, which
 * is the ADR-0003 two-phase gate this pass exists to prove, and both controls
 * appear in `tests/e2e/review-proposals.spec.ts:403`.
 */

/** How the spec builds a Playwright locator for an affordance. */
export type AffordanceSelector =
  /** A raw CSS selector, used verbatim. */
  | { kind: 'css'; value: string }
  /** `page.getByTestId(value)`. */
  | { kind: 'testId'; value: string }
  /**
   * `page.getByRole(role, { name })`. When `namePattern` is true, `name` is a
   * regular-expression SOURCE string (kept as a string so this module needs no
   * imports and stays trivially serialisable); `nameFlags` carries its flags.
   * `{boardName}` in `name` is substituted with the seeded board's name.
   */
  | {
      kind: 'role'
      role: string
      name: string
      exact?: boolean
      namePattern?: boolean
      nameFlags?: string
    }

/**
 * What must become observably true after the affordance is activated. `url` and
 * `response` pattern strings are regular-expression SOURCES; the `{boardId}`,
 * `{metricsBoardId}` and `{openingMonthLabel}` tokens in any of these strings
 * are substituted from the walk's runtime context before use.
 *
 * A `response` consequence proves an endpoint answered, which on its own cannot
 * tell the click's own request apart from one the view already had in flight.
 * Rows that can carry an independent DOM post-condition declare it in
 * `postCondition`, and the walk asserts it after the response.
 *
 * ADDING A KIND. Every kind here must be classified in
 * `CONSEQUENCE_ASSERTION_SITE` in `src/tests/guards/routeAffordanceCoverage.spec.ts`
 * (a `Record` keyed by this union, so a new kind fails `npm run typecheck`
 * until it is classified) and handled by `expectConsequence` in
 * `tests/e2e/route-affordances.spec.ts` (an exhaustive switch whose default
 * throws, because that file is not type-checked by any gate).
 */
export type AffordanceConsequence =
  | { kind: 'url'; pathPattern: string }
  | { kind: 'response'; method: 'GET' | 'POST' | 'PUT' | 'DELETE'; urlPattern: string }
  | { kind: 'node'; testId: string }
  | { kind: 'attribute'; selector: string; attribute: string; value: string }
  /** The named element holds focus, which is the whole job of a "jump me there" control. */
  | { kind: 'focus'; testId: string }
  /** The named control is present and enabled without being activated. */
  | { kind: 'enabled'; selector: string }
  /**
   * A form control's own DOM value. Read the note on the metrics rows before
   * trusting this to catch a dead control: when the walk drives a `<select>`
   * with `selectOption`, the browser sets that value itself, so the check
   * passes whether or not the control is wired to anything.
   */
  | { kind: 'value'; selector: string; value: string }
  /** The named node's rendered text, after token substitution. */
  | { kind: 'text'; selector: string; text: string }
  /**
   * The named node's rendered text must no longer read `from`. For a control
   * whose new text depends on the runtime clock (the calendar month label),
   * this is the strongest claim the inventory can make without re-implementing
   * the view's own date formatting inside the test.
   */
  | { kind: 'textChangedFrom'; selector: string; from: string }

/** Why a row is or is not activated by the walk. */
export type AffordanceStatus =
  | { activate: true }
  /** Another spec already clicks this control; `coveredBy` is `file:line`. */
  | { activate: false; reason: 'covered-elsewhere'; coveredBy: string }
  /** Irreversible: asserted present and enabled, never clicked. */
  | { activate: false; reason: 'guarded-not-activated'; assertEnabled: true }
  /** Reachable only from state no support helper can seed today. */
  | { activate: false; reason: 'out-of-slice-1'; missingState: string }

/** State the walk must have arranged before the affordance is reachable. */
export type AffordancePrecondition =
  | 'session'
  | 'seeded-board'
  | 'seeded-capture'
  | 'seeded-proposal'
  | 'empty-state'

export interface RouteAffordance {
  /** Unique across the whole inventory; the walk reports coverage by this id. */
  id: string
  label: string
  selector: AffordanceSelector
  /** `src/<path>:<line>` of the control's definition. */
  source: string
  precondition: AffordancePrecondition
  consequence: AffordanceConsequence
  /**
   * A second assertion, checked after `consequence`. It exists for `response`
   * rows: a network wait alone can be satisfied by a request the view issued on
   * mount, so a control that does nothing would still pass. Omitted where the
   * surface renders nothing that changes (see the notifications rows).
   *
   * HOW INDEPENDENT IT IS DEPENDS ON THE KIND. A `text` or `textChangedFrom`
   * post-condition reads state the view itself rendered, so it fails for a
   * control that did nothing (the calendar rows). A `value` post-condition on a
   * `<select>` the walk drove with `selectOption` does NOT: the browser assigns
   * the DOM value, so the check passes on a dead control too (the metrics rows,
   * whose real defences are documented there).
   */
  postCondition?: AffordanceConsequence
  status: AffordanceStatus
}

export interface RouteEntry {
  /** Must equal a `name` in the real `router.getRoutes()` table. */
  routeName: string
  /** Concrete path the walk navigates to; `{boardId}` is substituted. */
  entryPath: string
  /** Two to five affordances. Fewer says nothing; more is a second slice. */
  affordances: RouteAffordance[]
}

export interface ExcludedRoute {
  routeName: string
  /** At least 20 characters, enforced by the coverage guard. */
  reason: string
}

export const ROUTE_AFFORDANCE_INVENTORY: RouteEntry[] = [
  {
    routeName: 'workspace-home',
    entryPath: '/workspace/home',
    affordances: [
      {
        id: 'home.quick-capture-submit',
        label: 'Quick-capture input (Enter submits the capture row form)',
        selector: { kind: 'testId', value: 'paper-home-capture-input' },
        source: 'src/views/paper/PaperHomeView.vue:609',
        precondition: 'session',
        consequence: { kind: 'response', method: 'POST', urlPattern: '/api/capture/items$' },
        status: { activate: true },
      },
      {
        id: 'home.proposal-queue-card',
        label: 'Proposal queue card opens Review',
        selector: { kind: 'css', value: '[data-testid="paper-home-card-proposal"] button' },
        source: 'src/views/paper/PaperHomeView.vue:505',
        precondition: 'seeded-proposal',
        consequence: { kind: 'url', pathPattern: '/workspace/review$' },
        status: { activate: true },
      },
      {
        id: 'home.carryover-queue-card',
        label: 'Carry-over queue card opens Inbox',
        selector: { kind: 'css', value: '[data-testid="paper-home-card-carryover"] button' },
        source: 'src/views/paper/PaperHomeView.vue:505',
        precondition: 'seeded-capture',
        consequence: { kind: 'url', pathPattern: '/workspace/inbox$' },
        status: { activate: false, reason: 'covered-elsewhere', coveredBy: 'tests/e2e/capture-loop.spec.ts:55' },
      },
      {
        id: 'home.first-board-setup-cta',
        label: 'First-board setup CTA opens the workspace setup dialog',
        selector: { kind: 'testId', value: 'paper-home-setup-cta' },
        source: 'src/views/paper/PaperHomeView.vue:471',
        precondition: 'empty-state',
        consequence: { kind: 'node', testId: 'paper-home-first-board' },
        status: { activate: false, reason: 'covered-elsewhere', coveredBy: 'tests/e2e/first-run.spec.ts:49' },
      },
    ],
  },
  {
    routeName: 'workspace-today',
    entryPath: '/workspace/today',
    affordances: [
      {
        id: 'today.seal-request',
        label: 'Seal the day (opens the confirm group; writes nothing)',
        selector: { kind: 'css', value: '[data-action="seal"]' },
        source: 'src/views/paper/today/TodayCover.vue:121',
        precondition: 'session',
        consequence: { kind: 'node', testId: 'seal-confirm' },
        status: { activate: true },
      },
      {
        id: 'today.seal-confirm',
        label: 'Confirm seal — irreversible, asserted enabled and never clicked',
        selector: { kind: 'css', value: '[data-action="seal-confirm"]' },
        source: 'src/views/paper/today/TodayCover.vue:168',
        precondition: 'session',
        // Not the surrounding seal-confirm group: what has to be true is that
        // THIS control is a live choice the walk declines, not that its panel
        // rendered.
        consequence: { kind: 'enabled', selector: '[data-action="seal-confirm"]' },
        status: { activate: false, reason: 'guarded-not-activated', assertEnabled: true },
      },
      {
        id: 'today.write-note',
        label: 'Write a note focuses the line-for-tomorrow input',
        selector: { kind: 'css', value: '[data-action="note"]' },
        source: 'src/views/paper/today/TodayCover.vue:128',
        precondition: 'session',
        // The input renders unconditionally, so its presence proves nothing.
        // What the control actually does is move the caret: PaperTodayView.vue
        // :113-118 calls focus() on the line-for-tomorrow field.
        consequence: { kind: 'focus', testId: 'line-for-tomorrow-input' },
        status: { activate: true },
      },
    ],
  },
  {
    routeName: 'workspace-boards',
    entryPath: '/workspace/boards',
    affordances: [
      {
        id: 'boards.new-board-toggle',
        label: 'New Board reveals the create form',
        selector: { kind: 'role', role: 'button', name: '+ New Board' },
        source: 'src/views/BoardsListView.vue:91',
        precondition: 'session',
        consequence: { kind: 'attribute', selector: '#new-board-name', attribute: 'placeholder', value: 'Board name' },
        status: { activate: true },
      },
      {
        id: 'boards.create-submit',
        label: 'Create submits the new board and routes to it',
        selector: { kind: 'role', role: 'button', name: 'Create', exact: true },
        source: 'src/views/BoardsListView.vue:112',
        precondition: 'session',
        consequence: { kind: 'url', pathPattern: '/workspace/boards/[a-f0-9-]+$' },
        status: { activate: true },
      },
      {
        id: 'boards.open-board-card',
        label: 'Open board card routes to the board',
        selector: { kind: 'role', role: 'button', name: 'Open board: {boardName}' },
        source: 'src/views/BoardsListView.vue:172',
        precondition: 'seeded-board',
        consequence: { kind: 'url', pathPattern: '/workspace/boards/{boardId}$' },
        status: { activate: true },
      },
    ],
  },
  {
    routeName: 'workspace-board',
    entryPath: '/workspace/boards/{boardId}',
    affordances: [
      {
        id: 'board.capture-here',
        label: 'Capture here routes to the board-scoped Inbox',
        selector: { kind: 'role', role: 'button', name: 'Capture here' },
        source: 'src/views/paper/PaperBoardView.vue:782',
        precondition: 'seeded-board',
        consequence: { kind: 'url', pathPattern: '/workspace/inbox\\?boardId={boardId}$' },
        status: { activate: false, reason: 'covered-elsewhere', coveredBy: 'tests/e2e/review-proposals.spec.ts:75' },
      },
      {
        id: 'board.review',
        label: 'Review routes to the board-scoped Review queue',
        selector: { kind: 'role', role: 'button', name: 'Review', exact: true },
        source: 'src/views/paper/PaperBoardView.vue:783',
        precondition: 'seeded-board',
        consequence: { kind: 'url', pathPattern: '/workspace/review\\?boardId={boardId}$' },
        status: { activate: true },
      },
      {
        id: 'board.settings-opener',
        label: 'Board settings opens the settings dialog (opener only, never saved)',
        selector: { kind: 'testId', value: 'paper-board-settings' },
        source: 'src/views/paper/PaperBoardView.vue:779',
        precondition: 'seeded-board',
        consequence: { kind: 'node', testId: 'paper-board-dialog-name' },
        status: { activate: true },
      },
      {
        id: 'board.add-column-opener',
        label: 'Add column opens the inline column form (opener only, never submitted)',
        selector: { kind: 'testId', value: 'paper-board-add-column' },
        source: 'src/views/paper/PaperBoardView.vue:949',
        precondition: 'seeded-board',
        consequence: { kind: 'node', testId: 'paper-board-add-column-form' },
        status: { activate: true },
      },
      {
        id: 'board.add-card-opener',
        label: 'Add card opens the card composer (opener only, never submitted)',
        selector: { kind: 'css', value: '[data-action="toggle-add-card"]' },
        source: 'src/views/paper/PaperBoardColumn.vue:298',
        precondition: 'seeded-board',
        consequence: { kind: 'node', testId: 'paper-card-composer' },
        status: { activate: true },
      },
    ],
  },
  {
    routeName: 'workspace-inbox',
    entryPath: '/workspace/inbox',
    affordances: [
      {
        id: 'inbox.capture-submit',
        label: 'Composer Capture submits a capture item',
        selector: { kind: 'role', role: 'button', name: '^Capture', namePattern: true },
        source: 'src/views/paper/inbox/PaperCaptureComposer.vue:373',
        precondition: 'session',
        consequence: { kind: 'response', method: 'POST', urlPattern: '/api/capture/items$' },
        status: { activate: false, reason: 'covered-elsewhere', coveredBy: 'tests/e2e/review-proposals.spec.ts:88' },
      },
      {
        id: 'inbox.ask-ai-with-board',
        label: 'Ask AI on a row that already has a board queues triage directly',
        selector: { kind: 'css', value: '[data-action="accept"]' },
        source: 'src/views/paper/inbox/PaperTriageTable.vue:822',
        precondition: 'seeded-capture',
        consequence: { kind: 'response', method: 'POST', urlPattern: '/api/capture/items/[^/]+/triage$' },
        status: { activate: false, reason: 'covered-elsewhere', coveredBy: 'tests/e2e/review-proposals.spec.ts:97' },
      },
      {
        id: 'inbox.ask-ai-boardless-opens-picker',
        label: 'Ask AI on a BOARDLESS row opens the board picker with its confirm disabled (#1944)',
        selector: { kind: 'css', value: '[data-action="accept"]' },
        source: 'src/views/paper/inbox/PaperTriageTable.vue:822',
        precondition: 'seeded-capture',
        consequence: { kind: 'attribute', selector: '[data-action="accept-on-board"]', attribute: 'disabled', value: '' },
        status: { activate: true },
      },
      {
        id: 'inbox.archive',
        label: 'Archive files the capture away',
        selector: { kind: 'css', value: '[data-action="reject"]' },
        source: 'src/views/paper/inbox/PaperTriageTable.vue:838',
        precondition: 'seeded-capture',
        consequence: { kind: 'response', method: 'POST', urlPattern: '/api/capture/items/[^/]+/archive$' },
        status: { activate: true },
      },
      {
        id: 'inbox.variant-toggle-nib',
        label: 'Nib tab switches the capture composer variant',
        selector: { kind: 'role', role: 'tab', name: 'Nib' },
        source: 'src/views/paper/PaperInboxView.vue:568',
        precondition: 'session',
        consequence: { kind: 'attribute', selector: '.paper-inbox', attribute: 'data-variant', value: 'nib' },
        status: { activate: true },
      },
    ],
  },
  {
    routeName: 'workspace-review',
    entryPath: '/workspace/review',
    affordances: [
      {
        id: 'review.approve',
        label: 'Approve (phase 1 of the ADR-0003 two-phase apply)',
        selector: { kind: 'css', value: '[data-testid="decision-apply"][data-apply-phase="approve"]' },
        source: 'src/views/paper/review/ReviewDecisionRail.vue:252',
        precondition: 'seeded-proposal',
        consequence: { kind: 'response', method: 'POST', urlPattern: '/automation/proposals/[^/]+/approve$' },
        status: { activate: true },
      },
      {
        id: 'review.execute-opens-confirm',
        label: 'Execute (phase 2) opens the apply-to-board confirmation; the walk cancels it',
        selector: { kind: 'css', value: '[data-testid="decision-apply"][data-apply-phase="execute"]' },
        source: 'src/views/paper/review/ReviewDecisionRail.vue:252',
        precondition: 'seeded-proposal',
        consequence: { kind: 'node', testId: 'apply-confirm-dialog' },
        status: { activate: true },
      },
      {
        id: 'review.request-edit',
        label: 'Request edit opens the revision editor',
        selector: { kind: 'testId', value: 'decision-edit' },
        source: 'src/views/paper/review/ReviewDecisionRail.vue:235',
        precondition: 'seeded-proposal',
        consequence: { kind: 'node', testId: 'revision-editor' },
        status: { activate: true },
      },
      {
        id: 'review.queue-filter-pill',
        label: 'Queue filter pill re-filters the review queue',
        selector: { kind: 'css', value: '.paper-review-rail__filters button:last-child' },
        source: 'src/views/paper/review/ReviewQueueRail.vue:212',
        precondition: 'seeded-proposal',
        consequence: { kind: 'attribute', selector: '.paper-review-rail__filters button:last-child', attribute: 'aria-pressed', value: 'true' },
        status: { activate: true },
      },
      {
        id: 'review.clear-board-scope',
        label: 'Clear board scope drops the boardId query from Review',
        selector: { kind: 'testId', value: 'paper-review-clear-scope' },
        source: 'src/views/paper/PaperReviewView.vue:2922',
        precondition: 'seeded-board',
        consequence: { kind: 'url', pathPattern: '/workspace/review$' },
        status: { activate: true },
      },
    ],
  },
  {
    routeName: 'workspace-notifications',
    entryPath: '/workspace/notifications',
    affordances: [
      {
        id: 'notifications.refresh',
        label: 'Refresh re-reads the notification list',
        selector: { kind: 'role', role: 'button', name: 'Refresh' },
        source: 'src/views/NotificationInboxView.vue:233',
        precondition: 'session',
        // NO postCondition: a refresh that returns the same list changes nothing
        // on screen. The walk compensates by consuming the mount read before it
        // arms this wait, so only a second request can satisfy it.
        consequence: { kind: 'response', method: 'GET', urlPattern: '/api/notifications(\\?|$)' },
        status: { activate: true },
      },
      {
        id: 'notifications.unread-only',
        label: 'Show unread only re-reads the list with the filter applied',
        selector: { kind: 'role', role: 'checkbox', name: 'unread', namePattern: true, nameFlags: 'i' },
        source: 'src/views/NotificationInboxView.vue:241',
        precondition: 'session',
        // NO postCondition either: NotificationInboxView.vue:267 renders the same
        // "No notifications found." empty state filtered or not, and no helper
        // can seed a notification to tell the two lists apart.
        consequence: { kind: 'response', method: 'GET', urlPattern: '/api/notifications(\\?|$)' },
        status: { activate: true },
      },
      {
        id: 'notifications.mark-all-read',
        label: 'Mark all read',
        selector: { kind: 'role', role: 'button', name: 'Mark all read' },
        source: 'src/views/NotificationInboxView.vue:226',
        precondition: 'session',
        consequence: { kind: 'response', method: 'POST', urlPattern: '/api/notifications/mark-all-read' },
        status: {
          activate: false,
          reason: 'out-of-slice-1',
          missingState: 'the button renders only when unreadCount > 0 and no support helper can create an unread notification',
        },
      },
    ],
  },
  {
    routeName: 'workspace-calendar',
    entryPath: '/workspace/calendar',
    affordances: [
      {
        id: 'calendar.previous-month',
        label: 'Previous month re-reads the calendar window and moves the month label',
        selector: { kind: 'role', role: 'button', name: 'Previous month' },
        source: 'src/views/CalendarView.vue:218',
        precondition: 'session',
        // CalendarView fetches in `onMounted` and again on every `viewDate`
        // change (CalendarView.vue:178-179), so the walk consumes the mount read
        // before arming this one. The post-condition is the independent half:
        // the label is rendered from `viewDate` (CalendarView.vue:36-39), so a
        // button that did not move the month cannot satisfy it.
        consequence: { kind: 'response', method: 'GET', urlPattern: '/api/workspace/calendar' },
        postCondition: {
          kind: 'textChangedFrom',
          selector: '.paper-calendar__month-label',
          from: '{openingMonthLabel}',
        },
        status: { activate: true },
      },
      {
        id: 'calendar.next-month',
        label: 'Next month re-reads the calendar window and returns the month label',
        selector: { kind: 'role', role: 'button', name: 'Next month' },
        source: 'src/views/CalendarView.vue:227',
        precondition: 'session',
        // Walked immediately after `calendar.previous-month`, which is why the
        // post-condition can name an exact string: stepping forward from the
        // previous month lands back on the label the route opened with, with no
        // date formatting re-implemented in the test.
        consequence: { kind: 'response', method: 'GET', urlPattern: '/api/workspace/calendar' },
        postCondition: {
          kind: 'text',
          selector: '.paper-calendar__month-label',
          text: '{openingMonthLabel}',
        },
        status: { activate: true },
      },
      {
        id: 'calendar.timeline-mode',
        label: 'Timeline mode renders the timeline list',
        selector: { kind: 'role', role: 'button', name: 'Timeline' },
        source: 'src/views/CalendarView.vue:200',
        precondition: 'seeded-board',
        consequence: { kind: 'attribute', selector: '.paper-calendar__timeline', attribute: 'aria-label', value: 'Timeline' },
        status: {
          activate: false,
          reason: 'out-of-slice-1',
          missingState: 'the empty-state branch at src/views/CalendarView.vue:260 precedes BOTH view modes while totalCards is 0, and no support helper seeds a card carrying a due date, so the timeline list cannot render for a throwaway user',
        },
      },
    ],
  },
  {
    routeName: 'workspace-metrics',
    entryPath: '/workspace/metrics',
    affordances: [
      {
        id: 'metrics.board-select',
        label: 'Board select loads the metrics for that board',
        selector: { kind: 'css', value: '#board-select' },
        source: 'src/views/MetricsView.vue:167',
        precondition: 'seeded-board',
        // THE DEFENCES AGAINST A DEAD SELECT ARE THE PIN AND THE ANCHOR, not the
        // post-condition. The board id is the one the walk SELECTS, deliberately
        // not the one MetricsView auto-selects on mount (MetricsView.vue:86),
        // and `?from=` anchors the pattern to a metrics read; together they mean
        // only a request this selection caused can settle the wait.
        consequence: { kind: 'response', method: 'GET', urlPattern: '/api/metrics/boards/{metricsBoardId}\\?from=' },
        // A VALUE CHECK ONLY. `selectOption` makes the browser assign the DOM
        // value, and nothing re-renders it away, so this passes for a select
        // wired to nothing. It records that the walk drove the control it meant
        // to drive; it cannot catch a dead one.
        postCondition: { kind: 'value', selector: '#board-select', value: '{metricsBoardId}' },
        status: { activate: true },
      },
      {
        id: 'metrics.range-select',
        label: 'Date-range select re-reads metrics over the new window',
        selector: { kind: 'css', value: '#range-select' },
        source: 'src/views/MetricsView.vue:181',
        precondition: 'seeded-board',
        // Same two defences as the row above: the board id is pinned to the one
        // the walk selected (never the mount's auto-selection) and `?from=`
        // anchors the pattern to a metrics read, so this row can only be settled
        // by a request the range change caused.
        consequence: { kind: 'response', method: 'GET', urlPattern: '/api/metrics/boards/{metricsBoardId}\\?from=' },
        // A VALUE CHECK ONLY, for the same reason as the board select: the
        // browser sets `#range-select` to 90 whether or not the view listens.
        postCondition: { kind: 'value', selector: '#range-select', value: '90' },
        status: { activate: true },
      },
      {
        id: 'metrics.export-csv',
        label: 'Export CSV requests the export (the response is asserted, not the download)',
        selector: { kind: 'role', role: 'button', name: 'Export CSV', namePattern: true },
        source: 'src/views/MetricsView.vue:191',
        precondition: 'seeded-board',
        // No mount read touches /export, and a download leaves no DOM trace,
        // so the anchored response is the whole assertion.
        consequence: { kind: 'response', method: 'GET', urlPattern: '/api/metrics/boards/{metricsBoardId}/export\\?' },
        status: { activate: true },
      },
    ],
  },
  {
    routeName: 'workspace-integrations',
    entryPath: '/workspace/integrations',
    affordances: [
      {
        id: 'integrations.add-connector-opener',
        label: 'Add Connector opens the register form (opener only; registration is a write)',
        selector: { kind: 'role', role: 'button', name: '+ Add Connector' },
        source: 'src/views/IntegrationsView.vue:165',
        precondition: 'session',
        consequence: { kind: 'attribute', selector: '.paper-int__form', attribute: 'aria-label', value: 'Register a new connector' },
        status: { activate: true },
      },
      {
        id: 'integrations.markdown-import-link',
        label: 'Markdown import link routes to Export & Import settings',
        selector: { kind: 'role', role: 'link', name: 'Markdown import', namePattern: true },
        source: 'src/views/IntegrationsView.vue:183',
        precondition: 'session',
        consequence: { kind: 'url', pathPattern: '/workspace/settings/export-import$' },
        status: { activate: true },
      },
    ],
  },
  {
    routeName: 'workspace-views',
    entryPath: '/workspace/views',
    affordances: [
      {
        id: 'views.new-view-toggle',
        label: 'New View reveals the custom-view create form (opener only)',
        selector: { kind: 'role', role: 'button', name: 'New View' },
        source: 'src/views/SavedViewsView.vue:161',
        precondition: 'session',
        consequence: { kind: 'attribute', selector: '#sv-name', attribute: 'placeholder', value: 'e.g. My Blocked Tasks' },
        status: { activate: true },
      },
      {
        id: 'views.select-default-view',
        label: 'Selecting the Blocked Work default view routes to its detail path',
        selector: { kind: 'role', role: 'button', name: 'Blocked Work', namePattern: true },
        source: 'src/views/SavedViewsView.vue:262',
        precondition: 'session',
        consequence: { kind: 'url', pathPattern: '/workspace/views/default-blocked$' },
        status: { activate: true },
      },
    ],
  },
  {
    routeName: 'workspace-views-detail',
    entryPath: '/workspace/views/default-blocked',
    affordances: [
      {
        id: 'views-detail.clear-filter',
        label: 'Clear Filter returns to the unscoped Views list',
        selector: { kind: 'role', role: 'button', name: 'Clear Filter' },
        source: 'src/views/SavedViewsView.vue:167',
        precondition: 'session',
        consequence: { kind: 'url', pathPattern: '/workspace/views$' },
        status: { activate: true },
      },
      {
        id: 'views-detail.open-result-card',
        label: 'A result card routes to the board holding the card',
        selector: { kind: 'css', value: '.paper-views__result-card' },
        source: 'src/views/SavedViewsView.vue:332',
        precondition: 'seeded-board',
        consequence: { kind: 'url', pathPattern: '/workspace/boards/[a-f0-9-]+$' },
        status: {
          activate: false,
          reason: 'out-of-slice-1',
          missingState: 'no support helper seeds a card matching the Blocked Work filter (showBlockedOnly), so the result grid renders its empty state',
        },
      },
    ],
  },
]

/**
 * Every named `/workspace` route the walk deliberately does not enter, plus
 * `not-found` (the only other route carrying `requiresShell`). The coverage
 * guard asserts this list plus the inventory covers that surface exactly, in
 * both directions, so a new route cannot be added to the router without a
 * decision recorded here.
 */
export const EXCLUDED_WORKSPACE_ROUTES: ExcludedRoute[] = [
  {
    routeName: 'workspace-settings-profile',
    reason: 'Off the capture-to-board loop: profile settings write account state, not board or proposal state.',
  },
  {
    routeName: 'workspace-settings-access',
    reason: 'Every affordance needs a board already shared with a second collaborator. registerUserSession can mint the account, so the barrier is the sharing step, which no support helper performs; the surface renders empty without it.',
  },
  {
    routeName: 'workspace-settings-export-import',
    reason: 'Owns bulk import and export writes with no teardown helper, so a walk would leave data behind.',
  },
  {
    routeName: 'workspace-settings-preferences',
    reason: 'Off the capture-to-board loop: preference writes only change the walking session itself.',
  },
  {
    routeName: 'workspace-settings-appearance',
    reason: 'Covered by tests/e2e/appearance-segment-contrast.spec.ts and tests/e2e/dark-mode.spec.ts.',
  },
  {
    routeName: 'workspace-settings-api-keys',
    reason: 'Owns credential creation and revocation with no cleanup helper; a smoke pass must not mint keys.',
  },
  {
    routeName: 'workspace-agents',
    reason: 'No support helper can create an agent row, so every affordance sits behind an empty state.',
  },
  {
    routeName: 'workspace-agent-runs',
    reason: 'The path needs an agentId, and no support helper can create an agent or a run to supply one.',
  },
  {
    routeName: 'workspace-agent-run-detail',
    reason: 'The path needs both agentId and runId, and no support helper can create an agent run.',
  },
  {
    routeName: 'workspace-dev-tools',
    reason: 'The devTools flag defaults false and tests/e2e/support/authSession.ts omits it, so the router guard redirects to home.',
  },
  {
    routeName: 'workspace-activity',
    reason: 'Flag-gated activity surface already entered by tests/e2e/smoke.spec.ts:293.',
  },
  {
    routeName: 'workspace-activity-board',
    reason: 'Board-scoped variant of the activity surface covered at tests/e2e/smoke.spec.ts:293; adds no distinct affordance.',
  },
  {
    routeName: 'workspace-activity-entity',
    reason: 'Needs an entityType and entityId pair that no support helper produces; activity itself is covered at tests/e2e/smoke.spec.ts:293.',
  },
  {
    routeName: 'workspace-activity-user',
    reason: 'User-scoped variant of the activity surface covered at tests/e2e/smoke.spec.ts:293; adds no distinct affordance.',
  },
  {
    routeName: 'workspace-metrics-cohorts',
    reason: 'Renders the cohort dashboard, which needs cohort rows no support helper can seed.',
  },
  {
    routeName: 'workspace-automations-queue',
    reason: 'AutomationQueueView is the raw queue-REQUEST surface (queueStore rows, not proposals, and it renders no review rail): no E2E spec enters it and no support helper seeds a queue request, so every control sits behind an empty list.',
  },
  {
    routeName: 'workspace-automations-chat',
    reason: 'Covered by tests/e2e/automation-ops.spec.ts:56 and needs live LLM environment settings.',
  },
  {
    routeName: 'workspace-ops-cli',
    reason: 'Covered by tests/e2e/validation-ops-logs-health.spec.ts:14 through tests/e2e/support/opsConsole.ts.',
  },
  {
    routeName: 'workspace-ops-endpoints',
    reason: 'Flag-gated ops surface with no seeding helper; the ops console is exercised through its cli and logs tabs instead.',
  },
  {
    routeName: 'workspace-ops-logs',
    reason: 'Covered by tests/e2e/validation-ops-logs-health.spec.ts:72.',
  },
  {
    routeName: 'workspace-archive',
    reason: 'Covered by tests/e2e/smoke.spec.ts:474 and tests/e2e/validation-archive-recovery.spec.ts:144; it carries the #2163 race, so no new consumer is added.',
  },
  {
    routeName: 'not-found',
    reason: 'The catch-all /:pathMatch(.*)* route rather than a workspace surface. It DOES render two recovery links (NotFoundView.vue:18 and :21); they stay out of slice 1 because the route holds no state to walk and both links duplicate shell navigation already covered elsewhere.',
  },
]

/**
 * Unnamed records under `/workspace` in the real router table: `/workspace`,
 * `/workspace/activity/user/:userId`, `/workspace/automations` and
 * `/workspace/automations/proposals`. All four are pure redirects, so they own
 * no affordance and cannot appear in the inventory; the coverage guard pins the
 * count so a fifth redirect has to be accounted for deliberately.
 */
export const UNNAMED_WORKSPACE_REDIRECT_COUNT = 4
