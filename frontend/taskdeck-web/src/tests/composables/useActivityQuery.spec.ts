import { describe, expect, it, vi, beforeEach } from 'vitest'
import { reactive } from 'vue'
import { formatAction, formatTimestamp, normalizeEntityType } from '../../composables/useActivityQuery'

// --- Pure function tests (no Vue component mount required) ---

describe('formatAction', () => {
  it('returns string actions as-is', () => {
    expect(formatAction('Created')).toBe('Created')
    expect(formatAction('custom-action')).toBe('custom-action')
  })

  it('maps known numeric actions to labels', () => {
    expect(formatAction(0)).toBe('Created')
    expect(formatAction(1)).toBe('Updated')
    expect(formatAction(2)).toBe('Deleted')
    expect(formatAction(3)).toBe('Archived')
    expect(formatAction(4)).toBe('Unarchived')
    expect(formatAction(5)).toBe('Moved')
    expect(formatAction(6)).toBe('PermissionGranted')
    expect(formatAction(7)).toBe('PermissionRevoked')
    expect(formatAction(8)).toBe('OwnershipTransferred')
  })

  it('falls back to stringified number for unknown numeric actions', () => {
    expect(formatAction(99)).toBe('99')
    expect(formatAction(-1)).toBe('-1')
  })
})

describe('formatTimestamp', () => {
  it('returns a locale string for a valid ISO timestamp', () => {
    const result = formatTimestamp('2025-01-15T10:30:00Z')
    expect(typeof result).toBe('string')
    expect(result.length).toBeGreaterThan(0)
  })
})

describe('normalizeEntityType', () => {
  it('normalizes known entity types case-insensitively', () => {
    expect(normalizeEntityType('board')).toBe('Board')
    expect(normalizeEntityType('BOARD')).toBe('Board')
    expect(normalizeEntityType('Board')).toBe('Board')
    expect(normalizeEntityType('column')).toBe('Column')
    expect(normalizeEntityType('COLUMN')).toBe('Column')
    expect(normalizeEntityType('card')).toBe('Card')
    expect(normalizeEntityType('CARD')).toBe('Card')
    expect(normalizeEntityType('label')).toBe('Label')
    expect(normalizeEntityType('LABEL')).toBe('Label')
  })

  it('trims whitespace before normalizing', () => {
    expect(normalizeEntityType('  board  ')).toBe('Board')
    expect(normalizeEntityType('\tcard\n')).toBe('Card')
  })

  it('returns empty string for unknown entity types', () => {
    expect(normalizeEntityType('unknown')).toBe('')
    expect(normalizeEntityType('')).toBe('')
    expect(normalizeEntityType('comment')).toBe('')
  })
})

// --- Composable integration tests with store mocks ---

const mockRouter = {
  push: vi.fn().mockResolvedValue(undefined),
}

const mockRoute = reactive({
  name: 'workspace-activity',
  fullPath: '/workspace/activity',
  params: {} as Record<string, string>,
})

const mockAuditStore = reactive({
  entries: [] as Array<Record<string, unknown>>,
  loading: false,
  error: null as string | null,
  fetchBoardHistory: vi.fn().mockResolvedValue(undefined),
  fetchEntityHistory: vi.fn().mockResolvedValue(undefined),
  fetchUserHistory: vi.fn().mockResolvedValue(undefined),
})

const mockBoardStore = reactive({
  boards: [] as Array<{ id: string; name: string; isArchived: boolean }>,
  currentBoard: null as null | { id: string; columns: Array<{ id: string; name: string; position: number }> },
  currentBoardCards: [] as Array<{ id: string; title: string; columnId: string; position: number }>,
  currentBoardLabels: [] as Array<{ id: string; name: string }>,
  fetchBoards: vi.fn<(...args: unknown[]) => Promise<void>>(),
  fetchBoard: vi.fn<(boardId: string) => Promise<void>>(),
})

const mockSessionStore = reactive({
  userId: 'user-1',
  username: 'test-user',
})

const mockToastStore = {
  error: vi.fn(),
  success: vi.fn(),
}

vi.mock('vue-router', () => ({
  useRoute: () => mockRoute,
  useRouter: () => mockRouter,
}))

vi.mock('../../store/auditStore', () => ({
  useAuditStore: () => mockAuditStore,
}))

vi.mock('../../store/boardStore', () => ({
  useBoardStore: () => mockBoardStore,
}))

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: () => mockSessionStore,
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => mockToastStore,
}))

// useActivityQuery must be called inside a Vue setup context,
// so we use a minimal wrapper component to exercise it.
import { mount } from '@vue/test-utils'
import { defineComponent } from 'vue'
import { useActivityQuery } from '../../composables/useActivityQuery'

function mountWithQuery(onSetup?: (q: ReturnType<typeof useActivityQuery>) => void) {
  let query!: ReturnType<typeof useActivityQuery>
  const Wrapper = defineComponent({
    setup() {
      query = useActivityQuery()
      if (onSetup) onSetup(query)
      return {}
    },
    render() {
      return null
    },
  })
  const wrapper = mount(Wrapper)
  return { wrapper, query }
}

async function tick() {
  await Promise.resolve()
  await Promise.resolve()
  await Promise.resolve()
}

describe('useActivityQuery composable', () => {
  beforeEach(() => {
    vi.clearAllMocks()

    mockRoute.name = 'workspace-activity'
    mockRoute.fullPath = '/workspace/activity'
    mockRoute.params = {}

    mockAuditStore.entries = []
    mockAuditStore.loading = false
    mockAuditStore.error = null

    mockBoardStore.boards = []
    mockBoardStore.currentBoard = null
    mockBoardStore.currentBoardCards = []
    mockBoardStore.currentBoardLabels = []

    mockBoardStore.fetchBoards.mockImplementation(async () => {
      mockBoardStore.boards = [
        { id: 'board-1', name: 'Alpha', isArchived: false },
        { id: 'board-2', name: 'Beta', isArchived: false },
      ]
    })
    mockBoardStore.fetchBoard.mockResolvedValue(undefined)
  })

  it('defaults to board view mode', () => {
    const { query } = mountWithQuery()
    expect(query.viewMode.value).toBe('board')
  })

  it('computes boardOptions sorted by name', async () => {
    const { query } = mountWithQuery()
    await query.initialize()
    await tick()

    expect(query.boardOptions.value).toHaveLength(2)
    expect(query.boardOptions.value[0]!.label).toBe('Alpha')
    expect(query.boardOptions.value[1]!.label).toBe('Beta')
  })

  it('marks archived boards in label', async () => {
    mockBoardStore.fetchBoards.mockImplementation(async () => {
      mockBoardStore.boards = [
        { id: 'board-1', name: 'Done', isArchived: true },
      ]
    })

    const { query } = mountWithQuery()
    await query.initialize()
    await tick()

    expect(query.boardOptions.value[0]!.label).toBe('Done (Archived)')
  })

  it('canFetch is true when a board is selected in board mode', async () => {
    const { query } = mountWithQuery()
    await query.initialize()
    await tick()

    expect(query.canFetch.value).toBe(true)
    expect(query.selectedBoardId.value).toBe('board-1')
  })

  it('canFetch is false in entity mode without entity selected', async () => {
    const { query } = mountWithQuery()
    await query.initialize()
    await tick()

    query.viewMode.value = 'entity'
    await tick()

    // Entity type defaults to Board but entity ID may auto-select
    query.selectedEntityId.value = ''
    await tick()

    expect(query.canFetch.value).toBe(false)
  })

  it('canFetch is always true in user mode', async () => {
    const { query } = mountWithQuery()
    await query.initialize()
    await tick()

    query.viewMode.value = 'user'
    await tick()

    expect(query.canFetch.value).toBe(true)
  })

  it('selectedIdForCopy returns board ID in board mode', async () => {
    const { query } = mountWithQuery()
    await query.initialize()
    await tick()

    expect(query.selectedIdForCopy.value).toBe('board-1')
  })

  it('selectedIdForCopy returns user ID in user mode', async () => {
    const { query } = mountWithQuery()
    await query.initialize()
    await tick()

    query.viewMode.value = 'user'
    await tick()

    expect(query.selectedIdForCopy.value).toBe('user-1')
  })

  it('selectedIdLabel matches the current mode', async () => {
    const { query } = mountWithQuery()
    expect(query.selectedIdLabel.value).toBe('Board ID')

    query.viewMode.value = 'user'
    await tick()
    expect(query.selectedIdLabel.value).toBe('User ID')

    query.viewMode.value = 'entity'
    await tick()
    // defaults to Board entity type
    expect(query.selectedIdLabel.value).toContain('ID')
  })

  it('emptyStateTitle varies by mode', async () => {
    const { query } = mountWithQuery()

    expect(query.emptyStateTitle.value).toBe('No board activity yet')

    query.viewMode.value = 'entity'
    await tick()
    expect(query.emptyStateTitle.value).toBe('No entity activity yet')

    query.viewMode.value = 'user'
    await tick()
    expect(query.emptyStateTitle.value).toBe('No user activity yet')
  })

  it('emptyStateBody varies by mode', async () => {
    const { query } = mountWithQuery()

    expect(query.emptyStateBody.value).toContain('Choose another board')

    query.viewMode.value = 'entity'
    await tick()
    expect(query.emptyStateBody.value).toContain('Pick another entity')

    query.viewMode.value = 'user'
    await tick()
    expect(query.emptyStateBody.value).toContain('Activity will appear')
  })
})
