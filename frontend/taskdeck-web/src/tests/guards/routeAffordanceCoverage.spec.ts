import { describe, expect, it } from 'vitest'
import router from '../../router'
import {
  EXCLUDED_WORKSPACE_ROUTES,
  ROUTE_AFFORDANCE_INVENTORY,
  UNNAMED_WORKSPACE_REDIRECT_COUNT,
  type AffordanceConsequence,
  type ExcludedRoute,
  type RouteEntry,
} from '../../../tests/e2e/support/routeAffordanceInventory'

/**
 * GH-1949 AC4 — the coverage half of the route-walking affordance pass.
 *
 * WHAT THIS PROVES. The E2E walk in `tests/e2e/route-affordances.spec.ts` is
 * driven by a CLOSED, hand-authored inventory. A closed list has one failure
 * mode: someone adds a route to `src/router/index.ts` and nobody decides
 * whether it is walked. This spec reads the REAL router table — the same
 * `router.getRoutes()` the app runs, exactly as
 * `src/tests/router/workspaceRouteStability.spec.ts` does — and fails until
 * every shell route is either in the inventory or in the exclusion map with a
 * stated reason. There is no generated JSON in between, so the two cannot
 * drift apart silently.
 *
 * WHAT IT DOES NOT PROVE. Nothing here opens a browser. It does not prove a
 * selector matches, that an affordance works, or that an exclusion reason is
 * true — only that a decision was recorded for every route, that the shapes
 * are well formed, and that the checkers themselves can fail (assertions 7
 * and 9).
 *
 * IT ALSO GUARDS THE GUARDED ROWS. A `guarded-not-activated` row is asserted
 * and never clicked, so the walk can only check things that are true WITHOUT
 * activation. A `response` consequence on such a row would assert nothing at
 * all (the walk arms response waits around an activation it never performs).
 * Assertion 8 rejects that shape, and `CONSEQUENCE_ASSERTION_SITE` below makes
 * a new consequence kind fail `npm run typecheck` until someone classifies it.
 *
 * SHELL SURFACE. The compared set is "named routes whose meta carries
 * `requiresShell: true`". That is every `/workspace` route plus `not-found`,
 * which is `/:pathMatch(.*)*` but still renders inside the shell — so it is
 * recorded by name in the exclusion map rather than being silently dropped by
 * a path-prefix filter.
 */

type RouteName = string

function shellRouteNames(): RouteName[] {
  return router
    .getRoutes()
    .filter((route) => route.meta?.requiresShell === true && typeof route.name === 'string')
    .map((route) => route.name as RouteName)
}

/**
 * Assertion 3 as a callable, so assertion 7 can prove it is able to fail.
 * Throws with both directions named; returns nothing when the sets agree.
 */
function assertSurfaceIsFullyAccountedFor(
  inventory: RouteEntry[],
  excluded: ExcludedRoute[],
  actualNames: RouteName[],
): void {
  const declared = new Set<RouteName>([
    ...inventory.map((entry) => entry.routeName),
    ...excluded.map((entry) => entry.routeName),
  ])
  const actual = new Set<RouteName>(actualNames)

  const unaccountedFor = [...actual].filter((name) => !declared.has(name)).sort()
  const unknownToRouter = [...declared].filter((name) => !actual.has(name)).sort()

  if (unaccountedFor.length > 0 || unknownToRouter.length > 0) {
    throw new Error(
      'route-affordance inventory is out of step with the router table. '
        + `Shell routes not accounted for (add to the inventory or the exclusion map): [${unaccountedFor.join(', ')}]. `
        + `Declared names the router does not define (stale entry): [${unknownToRouter.join(', ')}].`,
    )
  }
}

/**
 * Where the walk checks each consequence kind.
 *
 *   'element' — asserted on a located node, so it holds for a row the walk only
 *               inspects (`tests/e2e/route-affordances.spec.ts`,
 *               `expectConsequence`).
 *   'page'    — asserted on the page's URL, which nothing changes unless the
 *               row is activated.
 *   'network' — asserted by a `waitForResponse` armed around the activation
 *               itself, so there is nothing to assert without one.
 *
 * Typed as a `Record` over the union on purpose: adding a kind to
 * `AffordanceConsequence` breaks this file's type-check until the new kind is
 * classified here, which is the same decision the walk's own switch has to make.
 */
const CONSEQUENCE_ASSERTION_SITE: Record<
  AffordanceConsequence['kind'],
  'element' | 'page' | 'network'
> = {
  url: 'page',
  response: 'network',
  node: 'element',
  attribute: 'element',
  focus: 'element',
  enabled: 'element',
  value: 'element',
  text: 'element',
  textChangedFrom: 'element',
}

/**
 * Assertion 8 as a callable, so assertion 9 can prove it is able to fail.
 * Throws naming the offending row; returns nothing when every guarded row is
 * assertable without being activated.
 */
function assertGuardedRowsAreAssertableWithoutActivation(inventory: RouteEntry[]): void {
  for (const entry of inventory) {
    for (const affordance of entry.affordances) {
      const status = affordance.status
      if (status.activate || status.reason !== 'guarded-not-activated') {
        continue
      }

      const site = CONSEQUENCE_ASSERTION_SITE[affordance.consequence.kind]
      if (site !== 'element') {
        throw new Error(
          `guarded row '${affordance.id}' (${entry.routeName}) declares a `
            + `'${affordance.consequence.kind}' consequence, which the walk asserts on the `
            + `${site}. A guarded-not-activated row is never clicked, so its consequence must be `
            + 'one the walk can assert on the located element. Declare an element consequence '
            + '(node, attribute, focus, enabled, value, text, textChangedFrom) instead.',
        )
      }

      // DEAD AT RUNTIME, AND THE TYPE IS WHY. `AffordanceStatus`'s
      // `guarded-not-activated` variant declares `assertEnabled: true` as a
      // REQUIRED LITERAL, so a row that reaches this line has already been
      // proved to satisfy it by `npm run typecheck`; this `!== true` can never
      // be true for a real inventory row. That is the difference from the
      // consequence check above, whose bad shape the type DOES permit (a
      // `response` consequence on a guarded row type-checks — assertion 9
      // builds exactly that row with no cast) and which therefore earns a
      // canary. This branch has none because there is no cast-free way to
      // reach it. It is kept as belt-and-braces: it is the check that starts
      // doing work the moment the field is widened to `boolean`.
      if (status.assertEnabled !== true) {
        throw new Error(
          `guarded row '${affordance.id}' (${entry.routeName}) must set assertEnabled: true. `
            + 'The walk asserts the control is a live choice it declines, not merely a node that '
            + 'rendered.',
        )
      }
    }
  }
}

describe('route affordance inventory coverage (GH-1949 AC4)', () => {
  // ── 1 ── the inventory says something, and each entry says enough ──────────
  it('lists at least one route and two to five affordances per route', () => {
    expect(ROUTE_AFFORDANCE_INVENTORY.length).toBeGreaterThan(0)

    for (const entry of ROUTE_AFFORDANCE_INVENTORY) {
      expect(
        entry.affordances.length,
        `${entry.routeName} declares ${entry.affordances.length} affordances; the inventory contract is two to five`,
      ).toBeGreaterThanOrEqual(2)
      expect(
        entry.affordances.length,
        `${entry.routeName} declares ${entry.affordances.length} affordances; more than five belongs in a second slice`,
      ).toBeLessThanOrEqual(5)
    }
  })

  // ── 2 ── every walked route name is a real route ──────────────────────────
  it('names only routes the real router table defines', () => {
    const allNames = router.getRoutes().map((route) => route.name)

    for (const entry of ROUTE_AFFORDANCE_INVENTORY) {
      expect(allNames, `inventory route '${entry.routeName}' is not in the router table`)
        .toContain(entry.routeName)
    }
  })

  // ── 3 ── the closed list is closed, in both directions ────────────────────
  it('accounts for every named shell route exactly once, in both directions', () => {
    const actual = shellRouteNames()

    // The only shell route outside /workspace is the catch-all. If a second one
    // appears, the exclusion map's framing needs revisiting, not just a new row.
    const nonWorkspaceShellRoutes = actual.filter((name) => {
      const record = router.getRoutes().find((route) => route.name === name)
      return record ? !record.path.startsWith('/workspace') : false
    })
    expect(nonWorkspaceShellRoutes).toEqual(['not-found'])

    expect(() =>
      assertSurfaceIsFullyAccountedFor(ROUTE_AFFORDANCE_INVENTORY, EXCLUDED_WORKSPACE_ROUTES, actual),
    ).not.toThrow()

    // No name may be both walked and excluded.
    const walked = new Set(ROUTE_AFFORDANCE_INVENTORY.map((entry) => entry.routeName))
    const bothWaysAtOnce = EXCLUDED_WORKSPACE_ROUTES
      .map((entry) => entry.routeName)
      .filter((name) => walked.has(name))
    expect(bothWaysAtOnce).toEqual([])
  })

  // ── 4 ── the unnamed redirect records are identified, not just counted ────
  /**
   * A count alone is satisfied by any four redirects, so swapping one for a
   * different path would pass. Pin the paths themselves; the count constant
   * then guards the arity of that list rather than standing in for it.
   */
  it('pins the identity of every unnamed /workspace redirect record', () => {
    const unnamedWorkspacePaths = router
      .getRoutes()
      .filter((route) => route.name === undefined && route.path.startsWith('/workspace'))
      .map((route) => route.path)
      .sort()

    expect(
      unnamedWorkspacePaths,
      'an unnamed /workspace record was added, removed or repointed. A redirect owns no '
        + 'affordance, so confirm that is still true and update this list and '
        + 'UNNAMED_WORKSPACE_REDIRECT_COUNT together',
    ).toEqual([
      '/workspace',
      '/workspace/activity/user/:userId',
      '/workspace/automations',
      '/workspace/automations/proposals',
    ])

    expect(unnamedWorkspacePaths).toHaveLength(UNNAMED_WORKSPACE_REDIRECT_COUNT)
  })

  // ── 5 ── an exclusion has to say why ──────────────────────────────────────
  it('gives every excluded route a substantive reason', () => {
    for (const entry of EXCLUDED_WORKSPACE_ROUTES) {
      expect(typeof entry.reason, `${entry.routeName} exclusion reason must be a string`).toBe('string')
      expect(
        entry.reason.trim().length,
        `${entry.routeName} exclusion reason is too short to be a reason: '${entry.reason}'`,
      ).toBeGreaterThanOrEqual(20)
    }
  })

  // ── 6 ── rows are addressable and traceable ───────────────────────────────
  it('gives every affordance a unique id and a src/<file>:<line> source', () => {
    const ids = ROUTE_AFFORDANCE_INVENTORY.flatMap((entry) =>
      entry.affordances.map((affordance) => affordance.id),
    )
    const duplicates = ids.filter((id, index) => ids.indexOf(id) !== index)
    expect(duplicates, `duplicate affordance ids: [${duplicates.join(', ')}]`).toEqual([])

    for (const entry of ROUTE_AFFORDANCE_INVENTORY) {
      for (const affordance of entry.affordances) {
        expect(
          affordance.source,
          `${affordance.id} source '${affordance.source}' must look like src/<file>:<line>`,
        ).toMatch(/^src\/.+:\d+$/)
      }
    }
  })

  // ── 7 ── the checker must be able to fail ─────────────────────────────────
  /**
   * Assertions 1-6 are worthless if assertion 3's comparison cannot go red. Drop
   * one walked route from a synthetic copy of the inventory and require the
   * checker to notice — the exact shape of "someone added a route and nobody
   * decided".
   */
  it('fails when a synthetic inventory drops one walked route', () => {
    const droppedEntry = ROUTE_AFFORDANCE_INVENTORY[0]
    expect(droppedEntry).toBeDefined()

    const syntheticInventory = ROUTE_AFFORDANCE_INVENTORY.slice(1)

    expect(() =>
      assertSurfaceIsFullyAccountedFor(syntheticInventory, EXCLUDED_WORKSPACE_ROUTES, shellRouteNames()),
    ).toThrow(new RegExp(`Shell routes not accounted for[^.]*${droppedEntry.routeName}`))
  })

  // ── 8 ── a guarded row must be assertable without being activated ─────────
  /**
   * The walk asserts `guarded-not-activated` rows and never clicks them, so a
   * `response` consequence copied onto one from an activated sibling would
   * assert NOTHING and still count as walked — the row would be reported as
   * covered while proving only that the control rendered.
   *
   * THE TWO HALVES ARE NOT ENFORCED THE SAME WAY. The consequence half is a
   * live check: a `response` consequence on a guarded row type-checks, so the
   * shape it rejects is one someone can actually write, which is why assertion
   * 9 below carries a canary for it. The `assertEnabled` half is NOT: the
   * guarded status variant declares `assertEnabled: true` as a required
   * literal, so `npm run typecheck` has already rejected every row this check
   * could catch, and its `!== true` throw cannot fire for a real row. Both the
   * checker here and the walk's `if (affordance.status.assertEnabled)` in
   * `tests/e2e/route-affordances.spec.ts` are kept as belt-and-braces, and
   * each says so at its own site. Neither describes a per-row choice — the
   * type forecloses the choice. Widening the field to `boolean` is what would
   * make them live, and would then owe this assertion a canary of its own.
   *
   * ELEMENT CONSEQUENCES ARE SAFE TO OFFER A GUARDED ROW, INCLUDING THE
   * NEGATED ONE. The permitted list below includes `textChangedFrom`, whose
   * assertion is `not.toHaveText`. A negated matcher would be worthless here
   * if it passed on a selector that matched nothing, but Playwright fails a
   * zero-element `not.toHaveText` with `element(s) not found` rather than
   * passing it — measured on 1.62.1 and recorded in full on the
   * `textChangedFrom` kind in `tests/e2e/support/routeAffordanceInventory.ts`.
   * So every kind classified `element` really does assert something about a
   * node that exists.
   */
  it('gives every guarded-not-activated row an element consequence and assertEnabled', () => {
    const guardedRows = ROUTE_AFFORDANCE_INVENTORY.flatMap((entry) =>
      entry.affordances.filter(
        (affordance) =>
          !affordance.status.activate && affordance.status.reason === 'guarded-not-activated',
      ),
    )
    expect(
      guardedRows.length,
      'the inventory has no guarded-not-activated rows left; if that is deliberate, this '
        + 'assertion and its canary can go with them',
    ).toBeGreaterThan(0)

    expect(() => assertGuardedRowsAreAssertableWithoutActivation(ROUTE_AFFORDANCE_INVENTORY))
      .not.toThrow()
  })

  // ── 9 ── assertion 8's checker must be able to fail ───────────────────────
  /**
   * The exact shape #2678 names: a future guarded row whose `response`
   * consequence was copied from an activated sibling. Fed a synthetic inventory
   * carrying one, the checker has to notice.
   */
  it('fails when a synthetic guarded row carries a response consequence', () => {
    const syntheticInventory: RouteEntry[] = [
      {
        routeName: 'workspace-today',
        entryPath: '/workspace/today',
        affordances: [
          {
            id: 'synthetic.guarded-response-row',
            label: 'A guarded row that asserts a response it can never observe',
            selector: { kind: 'css', value: '[data-action="seal-confirm"]' },
            source: 'src/views/paper/today/TodayCover.vue:168',
            precondition: 'session',
            consequence: { kind: 'response', method: 'POST', urlPattern: '/api/today/seal$' },
            status: { activate: false, reason: 'guarded-not-activated', assertEnabled: true },
          },
        ],
      },
    ]

    expect(() => assertGuardedRowsAreAssertableWithoutActivation(syntheticInventory))
      .toThrow(/synthetic\.guarded-response-row.*'response' consequence/s)
  })
})
