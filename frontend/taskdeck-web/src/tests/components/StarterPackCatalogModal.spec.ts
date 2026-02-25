import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import StarterPackCatalogModal from '../../components/board/StarterPackCatalogModal.vue'

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
  }),
}))

function buildCatalogEntry() {
  return {
    id: 'board-blueprint-engineering-sprint',
    category: 'board-blueprint',
    title: 'Board Blueprint - Engineering Sprint',
    summary: 'Sprint-ready engineering board',
    highlights: ['Sprint lane defaults'],
    manifest: {
      schemaVersion: '1.0',
      packId: 'board-blueprint-engineering-sprint',
      displayName: 'Board Blueprint - Engineering Sprint',
      compatibility: {
        minTaskdeckVersion: '1.0.0',
        requiredFeatures: ['boards', 'labels', 'cards'],
      },
      tags: ['starter', 'blueprint', 'engineering'],
      labels: [{ name: 'priority-high', color: '#E85D5D' }],
      columns: [{ name: 'Backlog', position: 0 }],
      templates: [],
      seedCards: [],
    },
  }
}

function buildResult(overrides?: Record<string, unknown>) {
  return {
    boardId: 'board-1',
    packId: 'board-blueprint-engineering-sprint',
    dryRun: false,
    applied: true,
    actions: [],
    conflicts: [],
    hasConflicts: false,
    hasBlockingConflicts: false,
    ...overrides,
  }
}

async function waitForUi() {
  await Promise.resolve()
  await Promise.resolve()
}

describe('StarterPackCatalogModal', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.getCatalog.mockResolvedValue([buildCatalogEntry()])
  })

  it('loads and renders starter-pack catalog entries from API', async () => {
    const wrapper = mount(StarterPackCatalogModal, {
      props: {
        boardId: 'board-1',
        isOpen: true,
      },
    })

    await waitForUi()

    expect(mocks.getCatalog).toHaveBeenCalledWith('board-1')
    expect(wrapper.text()).toContain('Starter Packs')
    expect(wrapper.text()).toContain('Board Blueprint - Engineering Sprint')
    expect(wrapper.text()).toContain('Preview Highlights')
  })

  it('shows empty state when API returns no catalog entries', async () => {
    mocks.getCatalog.mockResolvedValue([])

    const wrapper = mount(StarterPackCatalogModal, {
      props: {
        boardId: 'board-1',
        isOpen: true,
      },
    })

    await waitForUi()

    expect(wrapper.text()).toContain('No starter packs are currently available.')
  })

  it('shows empty search state when query has no matches', async () => {
    const wrapper = mount(StarterPackCatalogModal, {
      props: {
        boardId: 'board-1',
        isOpen: true,
      },
    })

    await waitForUi()
    await wrapper.get('#starter-pack-search').setValue('no-pack-like-this')
    await waitForUi()

    expect(wrapper.text()).toContain('No starter packs match this search.')
  })

  it('shows load error state when catalog request fails', async () => {
    mocks.getCatalog.mockRejectedValue(new Error('catalog unavailable'))

    const wrapper = mount(StarterPackCatalogModal, {
      props: {
        boardId: 'board-1',
        isOpen: true,
      },
    })

    await waitForUi()

    expect(wrapper.text()).toContain('catalog unavailable')
  })

  it('runs dry-run preview and shows result summary', async () => {
    mocks.applyStarterPack.mockResolvedValue(
      buildResult({
        dryRun: true,
        applied: false,
        actions: [{ entityType: 'label', operation: 'create', key: 'priority-high', reason: 'missing' }],
      })
    )

    const wrapper = mount(StarterPackCatalogModal, {
      props: {
        boardId: 'board-1',
        isOpen: true,
      },
    })

    await waitForUi()

    const previewButton = wrapper
      .findAll('button')
      .find((button) => button.text().includes('Preview (Dry Run)'))
    expect(previewButton).toBeTruthy()

    await previewButton!.trigger('click')
    await waitForUi()

    expect(mocks.applyStarterPack).toHaveBeenCalledWith(
      'board-1',
      expect.objectContaining({ dryRun: true })
    )
    expect(wrapper.text()).toContain('Dry-run Result')
    expect(wrapper.text()).toContain('Preview ready')
    expect(wrapper.text()).toContain('Planned create: 1')
    expect(wrapper.text()).not.toContain('Applied: 1')
  })

  it('applies selected pack in one click and refreshes board', async () => {
    mocks.applyStarterPack.mockResolvedValue(buildResult())

    const wrapper = mount(StarterPackCatalogModal, {
      props: {
        boardId: 'board-1',
        isOpen: true,
      },
    })

    await waitForUi()

    const applyButton = wrapper
      .findAll('button')
      .find((button) => button.text().includes('Apply Starter Pack'))
    expect(applyButton).toBeTruthy()

    await applyButton!.trigger('click')
    await waitForUi()

    expect(mocks.applyStarterPack).toHaveBeenCalledWith(
      'board-1',
      expect.objectContaining({ dryRun: false })
    )
    expect(mocks.fetchBoard).toHaveBeenCalledWith('board-1')
    expect(mocks.toastSuccess).toHaveBeenCalled()
    expect(mocks.toastWarning).not.toHaveBeenCalled()
    expect(wrapper.emitted('applied')).toBeTruthy()
  })

  it('applies with warning-first feedback when result contains only warning conflicts', async () => {
    mocks.applyStarterPack.mockResolvedValue(
      buildResult({
        actions: [
          { entityType: 'column', operation: 'create', key: 'Backlog', reason: 'Column will be created.' },
          {
            entityType: 'seedCard',
            operation: 'skip',
            key: 'Unresolvable seed @ Missing',
            reason: 'Seed card references unresolved metadata.',
          },
        ],
        conflicts: [
          {
            code: 'SeedCardColumnConflict',
            path: '$.seedCards[0].columnName',
            message: 'Seed card cannot resolve target column.',
            existingValue: null,
            incomingValue: 'Missing',
            severity: 'warning',
          },
        ],
        hasConflicts: true,
        hasBlockingConflicts: false,
      })
    )

    const wrapper = mount(StarterPackCatalogModal, {
      props: {
        boardId: 'board-1',
        isOpen: true,
      },
    })

    await waitForUi()

    const applyButton = wrapper
      .findAll('button')
      .find((button) => button.text().includes('Apply Starter Pack'))
    expect(applyButton).toBeTruthy()

    await applyButton!.trigger('click')
    await waitForUi()

    expect(mocks.fetchBoard).toHaveBeenCalledWith('board-1')
    expect(mocks.toastWarning).toHaveBeenCalled()
    expect(wrapper.emitted('applied')).toBeTruthy()
    expect(wrapper.text()).toContain('Applied with warnings')
    expect(wrapper.text()).toContain('Skipped: 1')
    expect(wrapper.text()).toContain('Warnings: 1')
  })

  it('renders conflict payload returned from apply endpoint', async () => {
    const conflictPayload = buildResult({
      applied: false,
      hasConflicts: true,
      hasBlockingConflicts: true,
      conflicts: [
        {
          code: 'ColumnPositionConflict',
          path: '$.columns[0].position',
          message: 'Position is already occupied',
          existingValue: 'Existing',
          incomingValue: 'Backlog',
          severity: 'blocking',
        },
      ],
    })

    mocks.applyStarterPack.mockRejectedValue({
      response: {
        status: 409,
        data: conflictPayload,
      },
    })

    const wrapper = mount(StarterPackCatalogModal, {
      props: {
        boardId: 'board-1',
        isOpen: true,
      },
    })

    await waitForUi()

    const applyButton = wrapper
      .findAll('button')
      .find((button) => button.text().includes('Apply Starter Pack'))
    expect(applyButton).toBeTruthy()

    await applyButton!.trigger('click')
    await waitForUi()

    expect(wrapper.text()).toContain('Blocked by conflicts')
    expect(wrapper.text()).toContain('Planned create: 0')
    expect(wrapper.text()).toContain('Blocking')
    expect(wrapper.text()).toContain('ColumnPositionConflict')
    expect(mocks.fetchBoard).not.toHaveBeenCalled()
  })
})
