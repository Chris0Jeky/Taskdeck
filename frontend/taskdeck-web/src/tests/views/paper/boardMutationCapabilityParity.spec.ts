import { createPinia, setActivePinia } from 'pinia'
import { beforeAll, describe, expect, it } from 'vitest'
import { useBoardStore } from '../../../store/boardStore'

/**
 * Board-mutation capability parity between the skins (#1945).
 *
 * The bug this exists to prevent already happened once. Every layer under the
 * UI was complete — authorized CRUD endpoints, api clients with passing specs,
 * store actions — and the canonical Paper skin simply never imported any of
 * it. `src/tests/api/*` stayed green throughout, because API coverage cannot
 * see a missing button. A future skin (or a refactor of this one) can drop the
 * same capabilities the same silent way.
 *
 * So this guard does not test behaviour. It walks each skin's component import
 * graph from its root view and asserts that every board-mutation action on the
 * `boardStore` facade is reachable from BOTH skins. Delete the column dialog
 * from Paper and the Paper set loses `updateColumn`/`deleteColumn` and this
 * goes red, naming them.
 *
 * Three deliberate design choices:
 *
 * 1. Sources come from Vite's raw glob, not `node:fs`. `tsconfig.vitest.json`
 *    deliberately omits node types (adding them breaks production source), and
 *    its quarantine list "may only shrink" — so a spec that needs `fs` and
 *    `process` cannot be type-checked here at all.
 * 2. The required set is DERIVED from the store, not hand-listed: structurally
 *    from which action group the facade delegates to, unioned with a name-shape
 *    backstop. A hand-written list has to be remembered; this one does not.
 * 3. Everything the derivation deliberately leaves out is named in
 *    `NON_BOARD_SURFACE_MUTATIONS` with a reason, so "why isn't X covered" has
 *    an answer in the file instead of in someone's memory.
 */

/** Raw source of every component/composable/store file, loaded on demand. */
const sources = import.meta.glob<string>('/src/**/*.{vue,ts}', {
  query: '?raw',
  import: 'default',
})

const SRC_PREFIX = '/src/'

const LEGACY_ROOT = 'views/BoardView.vue'
const PAPER_ROOT = 'views/paper/PaperBoardView.vue'
const FACADE = 'store/boardStore.ts'

/**
 * Layers the walk does not descend into. These are the layers the skins
 * CONSUME; following them would drag the whole store/api graph in and make the
 * extracted call set meaningless.
 */
const CONSUMED_LAYERS = ['store/', 'api/', 'types/', 'i18n/', 'locales/', 'utils/']

/**
 * Store actions that are deliberately NOT part of the board-surface capability
 * set, each with the reason it is out of scope. This is the only escape hatch;
 * everything else the derivation finds must be reachable from both skins.
 */
const NON_BOARD_SURFACE_MUTATIONS: Record<string, string> = {
  createBoard: 'boards LIST surface (BoardsListView), not an open board',
  updateFilters: 'client-side view state, no server write; Legacy-only FilterPanel',
  createLabel: 'label management modal — a Legacy-only surface, tracked separately',
  updateLabel: 'label management modal — a Legacy-only surface, tracked separately',
  deleteLabel: 'label management modal — a Legacy-only surface, tracked separately',
}

/**
 * Action-group factories in `store/board/*` whose writes a board surface must
 * be able to drive. Structural, so a NEW action added to one of these groups
 * joins the required set automatically — a `duplicateColumn` would, and no name
 * heuristic could be trusted to guess that verb in advance.
 */
const BOARD_SURFACE_GROUPS = ['boardCrud', 'columns', 'cards', 'comments']

/** Read-shaped names never count as capabilities to preserve. */
const READ_NAME = /^(fetch|get)[A-Z]/

/**
 * Backstop for the structural signal: a name that plainly says it writes counts
 * even if the facade stops routing it through a recognised group. The two
 * signals are UNIONed — either one is enough to require parity.
 */
const MUTATION_NAME = /^(create|update|delete|move|reorder)[A-Z]/

function sourceKey(relPath: string): string {
  return `${SRC_PREFIX}${relPath}`
}

async function readSource(relPath: string): Promise<string> {
  const loader = sources[sourceKey(relPath)]
  if (!loader) throw new Error(`No source loaded for "${relPath}"`)
  return loader()
}

/**
 * Resolve a RELATIVE import specifier against the importing file, the way Vite
 * would: try the literal path, then `.ts`, `.vue`, `/index.ts`. Bare
 * specifiers are packages and are never skin code, so they never get here.
 */
function resolveImport(fromRel: string, specifier: string): string | null {
  const parts = fromRel.split('/').slice(0, -1)
  for (const segment of specifier.split('/')) {
    if (segment === '' || segment === '.') continue
    if (segment === '..') parts.pop()
    else parts.push(segment)
  }
  const base = parts.join('/')
  const candidates = [base, `${base}.ts`, `${base}.vue`, `${base}/index.ts`]
  return candidates.find((candidate) => sourceKey(candidate) in sources) ?? null
}

type SkinGraph = { calls: Set<string>; files: string[] }

/**
 * Depth-first walk of a skin's own component graph.
 *
 * `excludedPrefixes` keeps the Legacy walk out of `views/paper/`: `BoardView`
 * is the shared shell that renders EITHER skin, so without it the Legacy graph
 * would swallow the Paper one and the guard could never tell them apart.
 */
async function walkSkin(rootRelative: string, excludedPrefixes: string[] = []): Promise<SkinGraph> {
  const calls = new Set<string>()
  const files: string[] = []
  const seen = new Set<string>()
  const queue = [rootRelative]

  while (queue.length > 0) {
    const file = queue.pop()
    if (!file || seen.has(file)) continue
    seen.add(file)

    if (CONSUMED_LAYERS.some((prefix) => file.startsWith(prefix))) continue
    if (excludedPrefixes.some((prefix) => file.startsWith(prefix))) continue

    files.push(file)
    const text = await readSource(file)

    for (const match of text.matchAll(/boardStore\.([A-Za-z0-9_]+)\s*\(/g)) {
      calls.add(match[1]!)
    }
    for (const match of text.matchAll(/from\s+['"](\.[^'"]+)['"]/g)) {
      const next = resolveImport(file, match[1]!)
      if (next) queue.push(next)
    }
  }

  return { calls, files: files.sort() }
}

/**
 * The extractor recognises `boardStore.<action>(` and nothing else. If a file
 * binds the store to another name the call becomes invisible and the guard
 * would go quietly green while missing coverage — the exact failure mode this
 * spec exists to stop. This turns that into a loud failure instead.
 */
async function bindingNames(files: string[]): Promise<string[]> {
  const names = new Set<string>()
  for (const file of files) {
    const text = await readSource(file)
    for (const match of text.matchAll(/(?:const|let)\s+([A-Za-z0-9_]+)\s*=\s*useBoardStore\s*\(/g)) {
      names.add(match[1]!)
    }
  }
  return [...names].sort()
}

/**
 * Map each key the `boardStore` facade returns to the action group it delegates
 * to, by reading the facade's own return block:
 *   `createColumn: columns.createColumn,` → createColumn → columns
 * Shorthand entries (`fetchBoard,`) have no group and fall through to the
 * name-shape signal alone.
 */
async function facadeGroups(): Promise<Map<string, string>> {
  const source = await readSource(FACADE)
  const returnBlock = source.slice(source.lastIndexOf('return {'))
  const groups = new Map<string, string>()
  for (const match of returnBlock.matchAll(
    /^\s*([A-Za-z0-9_]+):\s*([A-Za-z0-9_]+)\.[A-Za-z0-9_]+,\s*$/gm,
  )) {
    groups.set(match[1]!, match[2]!)
  }
  return groups
}

let legacy: SkinGraph
let paper: SkinGraph
let groups: Map<string, string>
let storeActions: string[] = []
let boardMutations: string[] = []

/** Every write the facade exposes, before the out-of-scope list is applied. */
function isBoardSurfaceWrite(action: string): boolean {
  if (READ_NAME.test(action)) return false
  const group = groups.get(action)
  return (group !== undefined && BOARD_SURFACE_GROUPS.includes(group)) || MUTATION_NAME.test(action)
}

beforeAll(async () => {
  legacy = await walkSkin(LEGACY_ROOT, ['views/paper/'])
  paper = await walkSkin(PAPER_ROOT)
  groups = await facadeGroups()

  setActivePinia(createPinia())
  const store = useBoardStore() as unknown as Record<string, unknown>
  storeActions = Object.keys(store)
    .filter((key) => typeof store[key] === 'function')
    .sort()
  boardMutations = storeActions
    .filter(isBoardSurfaceWrite)
    .filter((key) => !(key in NON_BOARD_SURFACE_MUTATIONS))
})

describe('board-mutation capability parity', () => {
  it('derives a non-empty board-mutation set from the live boardStore facade', () => {
    // Guards the guard: the parity assertions below iterate this list, so an
    // empty one would make them vacuously green.
    expect(boardMutations.length).toBeGreaterThanOrEqual(10)
    // Spot-check the four the issue was actually about, so a broken derivation
    // cannot quietly shrink the set to something that still passes the count.
    expect(boardMutations).toEqual(
      expect.arrayContaining(['createCard', 'updateColumn', 'deleteColumn', 'updateBoard']),
    )
  })

  it('walks a real graph for each skin rather than a single file', () => {
    // A resolver that silently returns null would leave one file per skin and
    // make every parity check meaningless.
    expect(legacy.files.length).toBeGreaterThan(10)
    expect(paper.files.length).toBeGreaterThan(10)
    // Transitive resolution works: both skins reach the SHARED card modal, and
    // its store calls only appear via `useCardModal`, two hops in.
    expect(legacy.files).toContain('components/board/CardModal.vue')
    expect(paper.files).toContain('components/board/CardModal.vue')
    expect(legacy.files).toContain('composables/useCardModal.ts')
    expect(paper.files).toContain('composables/useCardModal.ts')
    // The Legacy walk must not have swallowed the Paper subtree.
    expect(paper.files).toContain('views/paper/PaperBoardColumn.vue')
    expect(legacy.files.some((file) => file.startsWith('views/paper/'))).toBe(false)
  })

  it('reads the facade return block, so a restructure cannot mute the guard', () => {
    // The structural signal is only as good as this parse. If `boardStore.ts`
    // is restructured past the regex, the map empties, the group signal
    // silently degrades to the name heuristic, and a `duplicateColumn` would
    // slip through. Fail here instead.
    expect(groups.size).toBeGreaterThan(15)
    expect(groups.get('createColumn')).toBe('columns')
    expect(groups.get('moveCard')).toBe('cards')
    expect(groups.get('deleteBoard')).toBe('boardCrud')
    expect(groups.get('updateFilters')).toBe('filtering')
    for (const group of BOARD_SURFACE_GROUPS) {
      expect([...groups.values()], `no facade entry delegates to "${group}"`).toContain(group)
    }
  })

  it('binds the board store as `boardStore` everywhere the extractor reads', async () => {
    expect(await bindingNames(legacy.files)).toEqual(['boardStore'])
    expect(await bindingNames(paper.files)).toEqual(['boardStore'])
  })

  it('reaches every board-mutation action from the Legacy skin', () => {
    const missing = boardMutations.filter((action) => !legacy.calls.has(action))
    expect(missing, 'board mutations unreachable from views/BoardView.vue').toEqual([])
  })

  it('reaches every board-mutation action from the Paper skin', () => {
    // This is the assertion #1945 would have failed: before the port, Paper
    // reached only createColumn, moveCard, updateCard and deleteCard.
    const missing = boardMutations.filter((action) => !paper.calls.has(action))
    expect(missing, 'board mutations unreachable from views/paper/PaperBoardView.vue').toEqual([])
  })

  it('gives the two skins the same board-mutation capability set', () => {
    const legacySet = boardMutations.filter((action) => legacy.calls.has(action))
    const paperSet = boardMutations.filter((action) => paper.calls.has(action))
    expect(paperSet).toEqual(legacySet)
  })

  it('accounts for every board-surface write, in or out', () => {
    const allWrites = storeActions.filter(isBoardSurfaceWrite)

    const unaccounted = allWrites.filter(
      (action) => !boardMutations.includes(action) && !(action in NON_BOARD_SURFACE_MUTATIONS),
    )
    expect(
      unaccounted,
      'new board-surface store writes must either join the required set ' +
        'or be listed in NON_BOARD_SURFACE_MUTATIONS with a reason',
    ).toEqual([])

    // The exclusion list may not rot either: a name that no longer exists on
    // the store is a stale excuse hiding nothing.
    const staleExclusions = Object.keys(NON_BOARD_SURFACE_MUTATIONS).filter(
      (action) => !allWrites.includes(action),
    )
    expect(staleExclusions, 'NON_BOARD_SURFACE_MUTATIONS names a store action that is gone').toEqual(
      [],
    )
  })
})
