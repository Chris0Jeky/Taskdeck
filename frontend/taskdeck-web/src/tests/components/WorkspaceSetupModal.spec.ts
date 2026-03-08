import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import WorkspaceSetupModal from '../../components/workspace/WorkspaceSetupModal.vue'

const mocks = vi.hoisted(() => ({
  createBoard: vi.fn(),
  clearHomeSummary: vi.fn(),
  clearTodaySummary: vi.fn(),
  getCatalog: vi.fn(),
  applyStarterPack: vi.fn(),
  push: vi.fn(),
  toastSuccess: vi.fn(),
  toastWarning: vi.fn(),
}))

vi.mock('../../store/boardStore', () => ({
  useBoardStore: () => ({
    createBoard: mocks.createBoard,
  }),
}))

vi.mock('../../store/workspaceStore', () => ({
  useWorkspaceStore: () => ({
    clearHomeSummary: mocks.clearHomeSummary,
    clearTodaySummary: mocks.clearTodaySummary,
  }),
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => ({
    success: mocks.toastSuccess,
    warning: mocks.toastWarning,
  }),
}))

vi.mock('../../api/starterPacksApi', () => ({
  starterPacksApi: {
    getCatalog: mocks.getCatalog,
    applyStarterPack: mocks.applyStarterPack,
  },
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({
    push: mocks.push,
  }),
}))

async function waitForUi() {
  await Promise.resolve()
  await Promise.resolve()
}

describe('WorkspaceSetupModal', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.createBoard.mockResolvedValue({
      id: 'board-1',
      name: 'Product Sprint',
    })
    mocks.getCatalog.mockResolvedValue([
      {
        id: 'board-blueprint-engineering-sprint',
        title: 'Board Blueprint - Engineering Sprint',
        manifest: {
          schemaVersion: '1.0',
          packId: 'board-blueprint-engineering-sprint',
        },
      },
    ])
    mocks.applyStarterPack.mockResolvedValue({
      applied: true,
      hasConflicts: false,
      hasBlockingConflicts: false,
    })
  })

  it('creates a blank board and routes to it', async () => {
    const wrapper = mount(WorkspaceSetupModal, {
      props: {
        isOpen: true,
      },
    })

    await wrapper.get('input[placeholder="For example: Product Sprint"]').setValue('Product Sprint')
    await wrapper.get('.td-btn--primary').trigger('click')
    await waitForUi()

    expect(mocks.createBoard).toHaveBeenCalledWith({ name: 'Product Sprint' })
    expect(mocks.clearHomeSummary).toHaveBeenCalled()
    expect(mocks.clearTodaySummary).toHaveBeenCalled()
    expect(mocks.push).toHaveBeenCalledWith('/workspace/boards/board-1')
    expect(wrapper.emitted('created')?.[0]?.[0]).toEqual({ boardId: 'board-1', templateId: 'blank-board' })
  })

  it('applies the selected starter pack before routing', async () => {
    const wrapper = mount(WorkspaceSetupModal, {
      props: {
        isOpen: true,
      },
    })

    await wrapper.get('input[placeholder="For example: Product Sprint"]').setValue('Sprint Board')
    await wrapper.get('input[value="engineering-sprint"]').setValue(true)
    await wrapper.get('.td-btn--primary').trigger('click')
    await waitForUi()

    expect(mocks.getCatalog).toHaveBeenCalledWith('board-1')
    expect(mocks.applyStarterPack).toHaveBeenCalledWith(
      'board-1',
      expect.objectContaining({ dryRun: false }),
    )
    expect(mocks.toastSuccess).toHaveBeenCalled()
  })

  it('falls back to the created board when starter pack apply fails', async () => {
    mocks.applyStarterPack.mockRejectedValue(new Error('starter pack unavailable'))

    const wrapper = mount(WorkspaceSetupModal, {
      props: {
        isOpen: true,
      },
    })

    await wrapper.get('input[placeholder="For example: Product Sprint"]').setValue('Support Board')
    await wrapper.get('input[value="engineering-sprint"]').setValue(true)
    await wrapper.get('.td-btn--primary').trigger('click')
    await waitForUi()

    expect(mocks.toastWarning).toHaveBeenCalledWith(
      'starter pack unavailable. You can still finish setup from the board view.',
    )
    expect(mocks.push).toHaveBeenCalledWith('/workspace/boards/board-1')
  })
})
