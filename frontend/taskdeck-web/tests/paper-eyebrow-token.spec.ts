import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

import { describe, expect, it } from 'vitest'

/**
 * Canonical eyebrow token across the Paper view roots (#1842, ADR-0053).
 *
 * The Paper roots had split on this: seven roots tinted their page eyebrow
 * `--mute`, thirteen others `--ember`. PR #1808 declined to pick a side
 * unilaterally, so #1842's ruling settled it by FOLLOWING THE CORE LOOP rather
 * than by taste. Measured at `79428f0d` (this branch's parent):
 *
 *   - `src/paper-tokens.css:242-244` — `.paper .tk-eyebrow, .paper-night
 *     .tk-eyebrow { ...; color: var(--mute); ... }` is the base rule for the
 *     shared eyebrow utility, which every core-loop surface uses unmodified:
 *   - `src/views/paper/PaperHomeView.vue:337` (`tk-eyebrow paper-home__eyebrow`,
 *     whose rule at :540 sets only `text-transform`), plus `:414` and `:454`
 *   - `src/views/paper/PaperInboxView.vue:194` — bare `tk-eyebrow`
 *   - `src/views/paper/PaperBoardView.vue:256` — `paper-board-view__eyebrow`
 *     has no CSS rule at all
 *   - `src/views/paper/PaperReviewView.vue:1325`, `:1332` — bare `tk-eyebrow`
 *
 * Home, Inbox, Boards and Review therefore all render the eyebrow at `--mute`:
 * not one of them overrides the utility's colour. `--ember` stays reserved for
 * genuine accent/emphasis elements — `ReviewChangeSection`'s "after" eyebrow,
 * `ReviewKeysCard`, `PaperCardDetailView`'s banner eyebrow — which are
 * card-level accents rather than page wayfinding, and are deliberately outside
 * this guard's scope. So is `LoginView`/`RegisterView`'s `.td-auth-eyebrow`,
 * which is the pre-Paper auth shell's brand line, not a Paper view root.
 *
 * This spec pins the ruling so the split cannot silently reopen.
 *
 * It lives in the Node-flavoured frontend-root `tests/` lane, next to
 * `paper-legacy-bridge-invariants.spec.ts`, because it reads `.css` as well as
 * `.vue` sources off disk: `?raw` / `?inline` CSS imports resolve to `''` under
 * vitest's default `test.css: false`. That lane is not type-checked (#1607).
 */

const webRoot = resolve(fileURLToPath(import.meta.url), '..', '..')
const srcDir = resolve(webRoot, 'src')

/** The token every Paper page-header eyebrow must resolve to. */
const CANONICAL_EYEBROW_TOKEN = '--mute'

/** Its literal fallback, for Legacy ("Paper off") where Paper vars are absent. */
const CANONICAL_EYEBROW_FALLBACK = '#635c4e'

/**
 * Every Paper-idiom view root that has a page-header eyebrow. Mirrors the roots
 * in `src/tests/views/paperViewLegacySubstrate.spec.ts`, minus
 * `AutomationChatView` and `DevToolsView`, which have no eyebrow.
 */
const EYEBROW_ROOTS: ReadonlyArray<[view: string, rule: string]> = [
  ['ActivityView.vue', '.paper-activity__eyebrow'],
  ['AgentRunDetailView.vue', '.paper-run-detail__eyebrow'],
  ['AgentRunsView.vue', '.paper-agent-runs__eyebrow'],
  ['AgentsView.vue', '.paper-agents__eyebrow'],
  ['ApiKeySettingsView.vue', '.paper-api-keys__eyebrow'],
  ['AppearanceSettingsView.vue', '.paper-appearance__eyebrow'],
  ['ArchiveView.vue', '.paper-archive__eyebrow'],
  ['AutomationQueueView.vue', '.paper-queue__eyebrow'],
  ['BoardAccessView.vue', '.paper-access__eyebrow'],
  ['BoardsListView.vue', '.paper-boards__eyebrow'],
  ['CalendarView.vue', '.paper-calendar__eyebrow'],
  ['ExportImportView.vue', '.paper-portability__eyebrow'],
  ['IntegrationsView.vue', '.paper-int__eyebrow'],
  ['MetricsView.vue', '.paper-metrics__eyebrow'],
  ['NotFoundView.vue', '.paper-not-found__eyebrow'],
  ['NotificationInboxView.vue', '.paper-notifications__eyebrow'],
  ['NotificationPreferencesView.vue', '.paper-prefs__eyebrow'],
  ['OpsConsoleView.vue', '.paper-ops__eyebrow'],
  ['ProfileSettingsView.vue', '.paper-profile__eyebrow'],
  ['SavedViewsView.vue', '.paper-views__eyebrow'],
]

/** The four core-loop surfaces, and the eyebrow markup each was measured at. */
const CORE_LOOP_SURFACES: ReadonlyArray<[surface: string, file: string]> = [
  ['Home', 'src/views/paper/PaperHomeView.vue'],
  ['Inbox', 'src/views/paper/PaperInboxView.vue'],
  ['Boards', 'src/views/paper/PaperBoardView.vue'],
  ['Review', 'src/views/paper/PaperReviewView.vue'],
]

function readView(view: string): string {
  return readFileSync(resolve(srcDir, 'views', view), 'utf8')
}

/** The `color:` declaration inside a single-class rule block, or undefined. */
function eyebrowColor(source: string, rule: string): string | undefined {
  const escaped = rule.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  const block = new RegExp(`${escaped}\\s*\\{([^}]*)\\}`).exec(source)?.[1]
  if (block === undefined) return undefined
  return /color\s*:\s*([^;]+);/.exec(block)?.[1].trim()
}

describe('the core loop defines the canonical eyebrow token', () => {
  const tokens = readFileSync(resolve(srcDir, 'paper-tokens.css'), 'utf8')

  it('tints the shared .tk-eyebrow utility with --mute', () => {
    const block = /\.paper \.tk-eyebrow,\s*\.paper-night \.tk-eyebrow\s*\{([^}]*)\}/.exec(tokens)?.[1]
    expect(block, '.tk-eyebrow rule in paper-tokens.css').toBeDefined()
    expect(block).toMatch(new RegExp(`color:\\s*var\\(${CANONICAL_EYEBROW_TOKEN}\\)`))
  })

  it.each(CORE_LOOP_SURFACES)('%s uses tk-eyebrow without overriding its colour', (_surface, file) => {
    const source = readFileSync(resolve(webRoot, file), 'utf8')
    expect(source, `${file} renders no tk-eyebrow`).toMatch(/class="[^"]*\btk-eyebrow\b/)

    // Any `*__eyebrow` rule in a core-loop surface must leave `color` alone —
    // that is what makes the utility's `--mute` the measured core-loop token.
    for (const [, block] of source.matchAll(/\.[a-z0-9-]*__eyebrow[a-z0-9_-]*\s*\{([^}]*)\}/g)) {
      expect(block, `${file}: an eyebrow rule overrides colour`).not.toMatch(/(^|[;{\s])color\s*:/)
    }
  })
})

describe('every Paper view root uses the canonical eyebrow token', () => {
  it.each(EYEBROW_ROOTS)('%s tints %s with the canonical token', (view, rule) => {
    const color = eyebrowColor(readView(view), rule)
    expect(color, `${view}: no ${rule} { color: ... } rule found`).toBeDefined()
    expect(color, `${view} eyebrow`).toBe(
      `var(${CANONICAL_EYEBROW_TOKEN}, ${CANONICAL_EYEBROW_FALLBACK})`,
    )
  })

  it('leaves no Paper root tinting its eyebrow with the accent hue', () => {
    const deviating = EYEBROW_ROOTS.filter(([view, rule]) =>
      eyebrowColor(readView(view), rule)?.includes('--ember'),
    ).map(([view]) => view)
    expect(deviating).toEqual([])
  })
})
