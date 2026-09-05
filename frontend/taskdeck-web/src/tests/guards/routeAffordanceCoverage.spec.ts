import { describe, expect, it } from 'vitest'
import router from '../../router'
import {
  EXCLUDED_WORKSPACE_ROUTES,
  ROUTE_AFFORDANCE_INVENTORY,
  UNNAMED_WORKSPACE_REDIRECT_COUNT,
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
 * are well formed, and that the checker itself can fail (assertion 7).
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

  // ── 4 ── the unnamed redirect records are counted, not ignored ────────────
  it('pins the number of unnamed /workspace redirect records', () => {
    const unnamedWorkspaceRecords = router
      .getRoutes()
      .filter((route) => route.name === undefined && route.path.startsWith('/workspace'))

    expect(
      unnamedWorkspaceRecords.map((route) => route.path).sort(),
      'an unnamed /workspace record was added or removed; a redirect owns no affordance, '
        + 'so confirm that and update UNNAMED_WORKSPACE_REDIRECT_COUNT',
    ).toHaveLength(UNNAMED_WORKSPACE_REDIRECT_COUNT)
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
})
