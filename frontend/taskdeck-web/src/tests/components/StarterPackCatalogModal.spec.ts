import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import StarterPackCatalogModal from '../../components/board/StarterPackCatalogModal.vue'

const mocks = vi.hoisted(() => ({
  applyStarterPack: vi.fn(),
  fetchBoard: vi.fn(),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

vi.mock('../../api/starterPacksApi', () => ({
  starterPacksApi: {
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
  }),
}))

function buildResult(overrides?: Record<string, unknown>) {
  return {
    boardId: 'board-1',
    packId: 'engineering-onboarding',
    dryRun: false,
    applied: true,
    actions: [],
    conflicts: [],
    hasConflicts: false,
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
  })

  it('renders catalog entries and selected preview details', () => {
    const wrapper = mount(StarterPackCatalogModal, {
      props: {
        boardId: 'board-1',
        isOpen: true,
      },
    })

    expect(wrapper.text()).toContain('Starter Packs')
    expect(wrapper.text()).toContain('Engineering Onboarding')
    expect(wrapper.text()).toContain('Preview Highlights')
  })

  it('shows empty state when search has no matches', async () => {
    const wrapper = mount(StarterPackCatalogModal, {
      props: {
        boardId: 'board-1',
        isOpen: true,
      },
    })

    await wrapper.get('#starter-pack-search').setValue('no-pack-like-this')
    await waitForUi()

    expect(wrapper.text()).toContain('No starter packs match this search.')
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
    expect(wrapper.text()).toContain('No conflicts')
  })

  it('applies selected pack in one click and refreshes board', async () => {
    mocks.applyStarterPack.mockResolvedValue(buildResult())

    const wrapper = mount(StarterPackCatalogModal, {
      props: {
        boardId: 'board-1',
        isOpen: true,
      },
    })

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
    expect(wrapper.emitted('applied')).toBeTruthy()
  })

  it('renders conflict payload returned from apply endpoint', async () => {
    const conflictPayload = buildResult({
      applied: false,
      hasConflicts: true,
      conflicts: [
        {
          code: 'ColumnPositionConflict',
          path: '$.columns[0].position',
          message: 'Position is already occupied',
          existingValue: 'Existing',
          incomingValue: 'Backlog',
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

    const applyButton = wrapper
      .findAll('button')
      .find((button) => button.text().includes('Apply Starter Pack'))
    expect(applyButton).toBeTruthy()

    await applyButton!.trigger('click')
    await waitForUi()

    expect(wrapper.text()).toContain('ColumnPositionConflict')
    expect(mocks.fetchBoard).not.toHaveBeenCalled()
  })
})
