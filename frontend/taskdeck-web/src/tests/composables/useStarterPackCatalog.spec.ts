import { describe, it, expect, vi, beforeEach } from 'vitest'
import { useStarterPackCatalog } from '../../composables/useStarterPackCatalog'
import type {
  StarterPackApplyResult,
  StarterPackCatalogEntry,
  StarterPackManifest,
} from '../../types/starter-packs'

const mocks = vi.hoisted(() => ({
  getCatalog: vi.fn(),
  applyStarterPack: vi.fn(),
  fetchBoard: vi.fn(),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
  toastWarning: vi.fn(),
}))

vi.mock('../../api/starterPacksApi', () => ({
  starterPacksApi: {
    getCatalog: mocks.getCatalog,
    applyStarterPack: mocks.applyStarterPack,
    validateManifestJson: vi.fn(),
  },
}))

vi.mock('../../store/boardStore', () => ({
  useBoardStore: () => ({
    fetchBoard: mocks.fetchBoard,
  }),
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => ({
    success: mocks.toastSuccess,
    error: mocks.toastError,
    warning: mocks.toastWarning,
    info: vi.fn(),
  }),
}))

function makeManifest(overrides: Partial<StarterPackManifest> = {}): StarterPackManifest {
  return {
    schemaVersion: '1.0',
    packId: 'test-pack',
    displayName: 'Test Pack',
    description: 'A test manifest',
    compatibility: { minTaskdeckVersion: '1.0.0', requiredFeatures: ['boards'] },
    tags: ['starter', 'test'],
    labels: [{ name: 'bug', color: '#FF0000' }],
    columns: [{ name: 'Backlog', position: 0 }],
    templates: [],
    seedCards: [],
    ...overrides,
  }
}

function makeEntry(overrides: Partial<StarterPackCatalogEntry> = {}): StarterPackCatalogEntry {
  return {
    id: 'entry-1',
    category: 'board-blueprint',
    title: 'Test Entry',
    summary: 'A test entry for catalog',
    highlights: ['Highlight 1'],
    manifest: makeManifest(),
    ...overrides,
  }
}

function makeResult(overrides: Partial<StarterPackApplyResult> = {}): StarterPackApplyResult {
  return {
    boardId: 'b-1',
    packId: 'test-pack',
    dryRun: false,
    applied: true,
    actions: [],
    conflicts: [],
    ...overrides,
  }
}

describe('useStarterPackCatalog', () => {
  let onApplied: ReturnType<typeof vi.fn>

  beforeEach(() => {
    vi.clearAllMocks()
    onApplied = vi.fn()
  })

  describe('initial state', () => {
    it('starts with empty catalog and no selection', () => {
      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      expect(catalog.catalogEntries.value).toEqual([])
      expect(catalog.loadingCatalog.value).toBe(false)
      expect(catalog.catalogLoadError.value).toBeNull()
      expect(catalog.searchQuery.value).toBe('')
      expect(catalog.selectedPackId.value).toBe('')
      expect(catalog.errorMessage.value).toBeNull()
      expect(catalog.latestResult.value).toBeNull()
    })
  })

  describe('loadCatalog', () => {
    it('loads catalog entries and auto-selects the first', async () => {
      const entries = [makeEntry({ id: 'e1' }), makeEntry({ id: 'e2' })]
      mocks.getCatalog.mockResolvedValue(entries)

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()

      expect(mocks.getCatalog).toHaveBeenCalledWith('b-1')
      expect(catalog.catalogEntries.value).toHaveLength(2)
      expect(catalog.selectedPackId.value).toBe('e1')
      expect(catalog.loadingCatalog.value).toBe(false)
    })

    it('handles empty catalog', async () => {
      mocks.getCatalog.mockResolvedValue([])

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()

      expect(catalog.catalogEntries.value).toEqual([])
      expect(catalog.selectedPackId.value).toBe('')
    })

    it('handles API error and shows toast', async () => {
      mocks.getCatalog.mockRejectedValue(new Error('Network error'))

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()

      expect(catalog.catalogLoadError.value).toBe('Network error')
      expect(mocks.toastError).toHaveBeenCalledWith('Network error')
      expect(catalog.loadingCatalog.value).toBe(false)
    })

    it('clears previous state before loading', async () => {
      mocks.getCatalog.mockResolvedValue([makeEntry()])

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      catalog.searchQuery.value = 'old query'
      catalog.errorMessage.value = 'old error'

      await catalog.loadCatalog()

      // loadCatalog resets selectedPackId and clears feedback, but not searchQuery
      expect(catalog.errorMessage.value).toBeNull()
      expect(catalog.latestResult.value).toBeNull()
    })
  })

  describe('filteredPacks', () => {
    it('returns all entries when search query is empty', async () => {
      mocks.getCatalog.mockResolvedValue([makeEntry({ id: 'e1' }), makeEntry({ id: 'e2' })])

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()

      expect(catalog.filteredPacks.value).toHaveLength(2)
    })

    it('returns all entries when search query is only whitespace', async () => {
      mocks.getCatalog.mockResolvedValue([makeEntry()])

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      catalog.searchQuery.value = '   '

      expect(catalog.filteredPacks.value).toHaveLength(1)
    })

    it('filters entries by title', async () => {
      mocks.getCatalog.mockResolvedValue([
        makeEntry({ id: 'e1', title: 'Sprint Board' }),
        makeEntry({ id: 'e2', title: 'Kanban Board' }),
      ])

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      catalog.searchQuery.value = 'sprint'

      expect(catalog.filteredPacks.value).toHaveLength(1)
      expect(catalog.filteredPacks.value[0]!.id).toBe('e1')
    })

    it('filters by manifest tags', async () => {
      mocks.getCatalog.mockResolvedValue([
        makeEntry({ id: 'e1', manifest: makeManifest({ tags: ['engineering'] }) }),
        makeEntry({ id: 'e2', manifest: makeManifest({ tags: ['design'] }) }),
      ])

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      catalog.searchQuery.value = 'design'

      expect(catalog.filteredPacks.value).toHaveLength(1)
      expect(catalog.filteredPacks.value[0]!.id).toBe('e2')
    })

    it('filters by manifest description including null description', async () => {
      mocks.getCatalog.mockResolvedValue([
        makeEntry({ id: 'e1', manifest: makeManifest({ description: null }) }),
        makeEntry({ id: 'e2', manifest: makeManifest({ description: 'unique-keyword' }) }),
      ])

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      catalog.searchQuery.value = 'unique-keyword'

      // description ?? '' handles null
      expect(catalog.filteredPacks.value).toHaveLength(1)
      expect(catalog.filteredPacks.value[0]!.id).toBe('e2')
    })

    it('filters by summary and highlights', async () => {
      mocks.getCatalog.mockResolvedValue([
        makeEntry({ id: 'e1', summary: 'A basic board', highlights: ['Quick setup'] }),
        makeEntry({ id: 'e2', summary: 'Advanced workflow', highlights: ['CI/CD integration'] }),
      ])

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      catalog.searchQuery.value = 'CI/CD'

      expect(catalog.filteredPacks.value).toHaveLength(1)
      expect(catalog.filteredPacks.value[0]!.id).toBe('e2')
    })
  })

  describe('selectedPack', () => {
    it('returns null when filteredPacks is empty', async () => {
      mocks.getCatalog.mockResolvedValue([makeEntry({ id: 'e1', title: 'Sprint' })])

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      catalog.searchQuery.value = 'nothing-matches'

      expect(catalog.selectedPack.value).toBeNull()
    })

    it('returns the selected pack by ID', async () => {
      mocks.getCatalog.mockResolvedValue([
        makeEntry({ id: 'e1', title: 'First' }),
        makeEntry({ id: 'e2', title: 'Second' }),
      ])

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      catalog.selectPack('e2')

      expect(catalog.selectedPack.value?.id).toBe('e2')
    })

    it('falls back to first filtered pack when selected ID is not in filtered list', async () => {
      mocks.getCatalog.mockResolvedValue([
        makeEntry({ id: 'e1', title: 'Sprint Board' }),
        makeEntry({ id: 'e2', title: 'Kanban Board' }),
      ])

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      // selectedPackId is 'e1' (auto-selected)
      // Now filter so only e2 is visible
      catalog.searchQuery.value = 'kanban'

      // The watch on filteredPacks should update selectedPackId
      // But computed selectedPack falls back to first in filtered list
      expect(catalog.selectedPack.value?.id).toBe('e2')
    })
  })

  describe('selectPack', () => {
    it('selects a pack by ID and clears feedback', async () => {
      mocks.getCatalog.mockResolvedValue([makeEntry({ id: 'e1' }), makeEntry({ id: 'e2' })])

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      catalog.errorMessage.value = 'old error'

      catalog.selectPack('e2')

      expect(catalog.selectedPackId.value).toBe('e2')
      expect(catalog.errorMessage.value).toBeNull()
      expect(catalog.latestResult.value).toBeNull()
    })
  })

  describe('runPreview', () => {
    it('runs dry-run preview successfully', async () => {
      const previewResult = makeResult({
        dryRun: true,
        applied: false,
        actions: [{ entityType: 'label', operation: 'create', key: 'l1', reason: 'new' }],
      })
      mocks.getCatalog.mockResolvedValue([makeEntry()])
      mocks.applyStarterPack.mockResolvedValue(previewResult)

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      await catalog.runPreview()

      expect(mocks.applyStarterPack).toHaveBeenCalledWith('b-1', expect.objectContaining({ dryRun: true }))
      expect(catalog.latestResult.value).toEqual(previewResult)
      expect(mocks.toastSuccess).toHaveBeenCalledWith('Dry-run preview generated.')
      expect(catalog.runningPreview.value).toBe(false)
    })

    it('shows error toast for blocking conflicts in preview', async () => {
      mocks.getCatalog.mockResolvedValue([makeEntry()])
      mocks.applyStarterPack.mockResolvedValue(
        makeResult({
          dryRun: true,
          applied: false,
          hasBlockingConflicts: true,
          conflicts: [{ code: 'C', path: '$', message: 'blocked', existingValue: null, incomingValue: null, severity: 'blocking' }],
        }),
      )

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      await catalog.runPreview()

      expect(mocks.toastError).toHaveBeenCalledWith('Dry-run found blocking starter pack conflicts.')
    })

    it('shows warning toast for warning conflicts in preview', async () => {
      mocks.getCatalog.mockResolvedValue([makeEntry()])
      mocks.applyStarterPack.mockResolvedValue(
        makeResult({
          dryRun: true,
          applied: false,
          conflicts: [{ code: 'C', path: '$', message: 'warn', existingValue: null, incomingValue: null, severity: 'warning' }],
        }),
      )

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      await catalog.runPreview()

      expect(mocks.toastWarning).toHaveBeenCalledWith('Dry-run found warnings. Review skipped or unresolved items.')
    })

    it('shows warning toast when preview has skip actions', async () => {
      mocks.getCatalog.mockResolvedValue([makeEntry()])
      mocks.applyStarterPack.mockResolvedValue(
        makeResult({
          dryRun: true,
          applied: false,
          actions: [{ entityType: 'card', operation: 'skip', key: 'k', reason: 'exists' }],
        }),
      )

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      await catalog.runPreview()

      expect(mocks.toastWarning).toHaveBeenCalledWith('Dry-run found warnings. Review skipped or unresolved items.')
    })

    it('handles preview API error', async () => {
      mocks.getCatalog.mockResolvedValue([makeEntry()])
      mocks.applyStarterPack.mockRejectedValue(new Error('preview failed'))

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      await catalog.runPreview()

      expect(catalog.errorMessage.value).toBe('preview failed')
      expect(mocks.toastError).toHaveBeenCalledWith('preview failed')
      expect(catalog.runningPreview.value).toBe(false)
    })

    it('does nothing when no pack is selected', async () => {
      mocks.getCatalog.mockResolvedValue([])

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      await catalog.runPreview()

      expect(mocks.applyStarterPack).not.toHaveBeenCalled()
    })

    it('does nothing when already running preview', async () => {
      mocks.getCatalog.mockResolvedValue([makeEntry()])
      mocks.applyStarterPack.mockImplementation(() => new Promise(() => {})) // never resolves

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()

      // Start first preview (it never resolves)
      const _firstPreview = catalog.runPreview()
      // Try to start second - should be blocked
      await catalog.runPreview()

      // Only one call should have been made
      expect(mocks.applyStarterPack).toHaveBeenCalledTimes(1)

      // Clean up by resetting the mock
      mocks.applyStarterPack.mockResolvedValue(makeResult({ dryRun: true }))
    })
  })

  describe('applyPack', () => {
    it('applies pack successfully and refreshes board', async () => {
      mocks.getCatalog.mockResolvedValue([makeEntry()])
      mocks.applyStarterPack.mockResolvedValue(makeResult())

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      await catalog.applyPack()

      expect(mocks.applyStarterPack).toHaveBeenCalledWith('b-1', expect.objectContaining({ dryRun: false }))
      expect(mocks.fetchBoard).toHaveBeenCalledWith('b-1')
      expect(onApplied).toHaveBeenCalled()
      expect(mocks.toastSuccess).toHaveBeenCalled()
      expect(catalog.applyingPack.value).toBe(false)
    })

    it('shows warning toast when applied with warning conflicts', async () => {
      mocks.getCatalog.mockResolvedValue([makeEntry()])
      mocks.applyStarterPack.mockResolvedValue(
        makeResult({
          applied: true,
          conflicts: [{ code: 'C', path: '$', message: 'w', existingValue: null, incomingValue: null, severity: 'warning' }],
        }),
      )

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      await catalog.applyPack()

      expect(mocks.toastWarning).toHaveBeenCalled()
      expect(mocks.fetchBoard).toHaveBeenCalled()
      expect(onApplied).toHaveBeenCalled()
    })

    it('shows warning toast when applied with skip actions', async () => {
      mocks.getCatalog.mockResolvedValue([makeEntry()])
      mocks.applyStarterPack.mockResolvedValue(
        makeResult({
          applied: true,
          actions: [{ entityType: 'card', operation: 'skip', key: 'k', reason: 'r' }],
        }),
      )

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      await catalog.applyPack()

      expect(mocks.toastWarning).toHaveBeenCalled()
    })

    it('does not apply when result has blocking conflicts', async () => {
      mocks.getCatalog.mockResolvedValue([makeEntry()])
      mocks.applyStarterPack.mockResolvedValue(
        makeResult({
          applied: false,
          hasBlockingConflicts: true,
          conflicts: [{ code: 'C', path: '$', message: 'blocked', existingValue: null, incomingValue: null, severity: 'blocking' }],
        }),
      )

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      await catalog.applyPack()

      expect(mocks.toastError).toHaveBeenCalledWith('Starter pack apply is blocked by conflicts.')
      expect(mocks.fetchBoard).not.toHaveBeenCalled()
      expect(onApplied).not.toHaveBeenCalled()
    })

    it('handles 409 conflict error response', async () => {
      const conflictPayload = makeResult({
        applied: false,
        hasBlockingConflicts: true,
        conflicts: [{ code: 'C', path: '$', message: 'blocked', existingValue: null, incomingValue: null, severity: 'blocking' }],
      })
      mocks.getCatalog.mockResolvedValue([makeEntry()])
      mocks.applyStarterPack.mockRejectedValue({
        response: { status: 409, data: conflictPayload },
      })

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      await catalog.applyPack()

      expect(catalog.latestResult.value).toEqual(conflictPayload)
      expect(mocks.toastError).toHaveBeenCalledWith('Starter pack apply is blocked by conflicts.')
      expect(mocks.fetchBoard).not.toHaveBeenCalled()
    })

    it('handles non-409 error response', async () => {
      mocks.getCatalog.mockResolvedValue([makeEntry()])
      mocks.applyStarterPack.mockRejectedValue(new Error('server error'))

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      await catalog.applyPack()

      expect(catalog.errorMessage.value).toBe('server error')
      expect(mocks.toastError).toHaveBeenCalledWith('server error')
    })

    it('does nothing when no pack is selected', async () => {
      mocks.getCatalog.mockResolvedValue([])

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      await catalog.applyPack()

      expect(mocks.applyStarterPack).not.toHaveBeenCalled()
    })

    it('shows warning when applied is false (no changes)', async () => {
      mocks.getCatalog.mockResolvedValue([makeEntry()])
      mocks.applyStarterPack.mockResolvedValue(makeResult({ applied: false }))

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      await catalog.applyPack()

      expect(mocks.toastWarning).toHaveBeenCalled()
      expect(mocks.fetchBoard).not.toHaveBeenCalled()
      expect(onApplied).not.toHaveBeenCalled()
    })
  })

  describe('extractConflictResult (tested via applyPack)', () => {
    it('returns null for non-object error', async () => {
      mocks.getCatalog.mockResolvedValue([makeEntry()])
      mocks.applyStarterPack.mockRejectedValue('string error')

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      await catalog.applyPack()

      // Should fall through to the generic error handler
      expect(catalog.latestResult.value).toBeNull()
    })

    it('returns null for null error', async () => {
      mocks.getCatalog.mockResolvedValue([makeEntry()])
      mocks.applyStarterPack.mockRejectedValue(null)

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      await catalog.applyPack()

      expect(catalog.latestResult.value).toBeNull()
    })

    it('returns null for non-409 response status', async () => {
      mocks.getCatalog.mockResolvedValue([makeEntry()])
      mocks.applyStarterPack.mockRejectedValue({
        response: { status: 500, data: {} },
        message: 'Internal Server Error',
      })

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      await catalog.applyPack()

      // Falls through to generic error path
      expect(catalog.latestResult.value).toBeNull()
    })

    it('returns null for 409 with non-object data', async () => {
      mocks.getCatalog.mockResolvedValue([makeEntry()])
      mocks.applyStarterPack.mockRejectedValue({
        response: { status: 409, data: 'not an object' },
        message: 'Conflict',
      })

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      await catalog.applyPack()

      expect(catalog.latestResult.value).toBeNull()
    })

    it('returns null for 409 with null data', async () => {
      mocks.getCatalog.mockResolvedValue([makeEntry()])
      mocks.applyStarterPack.mockRejectedValue({
        response: { status: 409, data: null },
        message: 'Conflict',
      })

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      await catalog.applyPack()

      expect(catalog.latestResult.value).toBeNull()
    })

    it('returns null for 409 with incomplete payload (missing boardId)', async () => {
      mocks.getCatalog.mockResolvedValue([makeEntry()])
      mocks.applyStarterPack.mockRejectedValue({
        response: {
          status: 409,
          data: { packId: 'x', dryRun: false, applied: false, actions: [], conflicts: [] },
        },
        message: 'Conflict',
      })

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      await catalog.applyPack()

      expect(catalog.latestResult.value).toBeNull()
    })

    it('returns null for 409 with payload where actions is not an array', async () => {
      mocks.getCatalog.mockResolvedValue([makeEntry()])
      mocks.applyStarterPack.mockRejectedValue({
        response: {
          status: 409,
          data: { boardId: 'b', packId: 'p', dryRun: false, applied: false, actions: 'not-array', conflicts: [] },
        },
        message: 'Conflict',
      })

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      await catalog.applyPack()

      expect(catalog.latestResult.value).toBeNull()
    })

    it('returns null for 409 with payload where conflicts is not an array', async () => {
      mocks.getCatalog.mockResolvedValue([makeEntry()])
      mocks.applyStarterPack.mockRejectedValue({
        response: {
          status: 409,
          data: { boardId: 'b', packId: 'p', dryRun: false, applied: false, actions: [], conflicts: 'not-array' },
        },
        message: 'Conflict',
      })

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      await catalog.applyPack()

      expect(catalog.latestResult.value).toBeNull()
    })

    it('returns null for error without response property', async () => {
      mocks.getCatalog.mockResolvedValue([makeEntry()])
      mocks.applyStarterPack.mockRejectedValue({ message: 'no response' })

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      await catalog.applyPack()

      expect(catalog.latestResult.value).toBeNull()
    })
  })

  describe('reset', () => {
    it('clears search, selection, and feedback', async () => {
      mocks.getCatalog.mockResolvedValue([makeEntry()])

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      catalog.searchQuery.value = 'test'
      catalog.errorMessage.value = 'some error'

      catalog.reset()

      expect(catalog.searchQuery.value).toBe('')
      expect(catalog.selectedPackId.value).toBe('')
      expect(catalog.errorMessage.value).toBeNull()
      expect(catalog.latestResult.value).toBeNull()
    })
  })

  describe('clearFeedback', () => {
    it('clears error and result state', async () => {
      mocks.getCatalog.mockResolvedValue([makeEntry()])

      const catalog = useStarterPackCatalog(() => 'b-1', onApplied)
      await catalog.loadCatalog()
      catalog.errorMessage.value = 'error'
      catalog.latestResult.value = makeResult()

      catalog.clearFeedback()

      expect(catalog.errorMessage.value).toBeNull()
      expect(catalog.latestResult.value).toBeNull()
    })
  })
})
