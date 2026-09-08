import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { reactive } from 'vue'
import WorkspaceMemoryView from '../../views/WorkspaceMemoryView.vue'
import type { Board } from '../../types/board'
import type { Memory } from '../../types/workspaceInsights'

const mocks = vi.hoisted(() => ({
  route: { query: {} as Record<string, string> },
  boardStore: { boards: [] as Board[], fetchBoards: vi.fn() },
  api: {
    getMemories: vi.fn(),
    createMemory: vi.fn(),
    updateMemory: vi.fn(),
    setMemoryArchived: vi.fn(),
  },
}))

const routeMock = reactive(mocks.route)
const boardStore = reactive(mocks.boardStore)
const api = mocks.api

vi.mock('vue-router', () => ({
  useRoute: () => mocks.route,
  RouterLink: {
    props: ['to'],
    template: '<a :href="typeof to === \'string\' ? to : \'#\'"><slot /></a>',
  },
}))

vi.mock('../../store/boardStore', () => ({
  useBoardStore: () => mocks.boardStore,
}))

vi.mock('../../api/workspaceInsights', () => ({
  workspaceInsightsApi: mocks.api,
}))

const board: Board = {
  id: 'board-1',
  name: 'Product board',
  description: null,
  isArchived: false,
  createdAt: '2026-09-01T00:00:00Z',
  updatedAt: '2026-09-01T00:00:00Z',
}

const memory: Memory = {
  id: 'memory-1',
  boardId: board.id,
  title: 'Release notes context',
  text: 'Copy review is owned by design.',
  originalText: 'Copy review is owned by design.',
  originalEvidence: 'The card is blocked by copy review.',
  status: 'statement',
  archived: false,
  revision: 2,
  createdAt: '2026-09-08T09:01:00Z',
  history: [
    {
      title: 'Release notes context',
      text: 'Copy review is still being assigned.',
      status: 'assumption',
      revision: 1,
      recordedAt: '2026-09-07T09:01:00Z',
    },
  ],
}

async function settle() {
  await flushPromises()
  await flushPromises()
}

describe('WorkspaceMemoryView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    routeMock.query = {}
    boardStore.boards = [board]
    boardStore.fetchBoards.mockResolvedValue(undefined)
    api.getMemories.mockResolvedValue([memory])
    api.createMemory.mockResolvedValue({ ...memory, id: 'memory-2', revision: 1 })
    api.updateMemory.mockResolvedValue(memory)
    api.setMemoryArchived.mockResolvedValue({ ...memory, archived: true })
  })

  it('loads board memory and exposes revision history', async () => {
    const wrapper = mount(WorkspaceMemoryView)
    await settle()

    expect(api.getMemories).toHaveBeenCalledWith('board-1', false)
    expect(wrapper.text()).toContain(memory.title)
    expect(wrapper.text()).toContain('Revision 2')
    expect(wrapper.text()).toContain('Revision history (1)')
    expect(wrapper.text()).toContain('Copy review is still being assigned.')
  })

  it('creates a private memory for the selected board', async () => {
    const wrapper = mount(WorkspaceMemoryView)
    await settle()
    await wrapper.get('[data-action="new-memory"]').trigger('click')
    await wrapper.get('#memory-title').setValue('New context')
    await wrapper.get('#memory-text').setValue('A durable note for this board.')
    await wrapper.get('form').trigger('submit')
    await settle()

    expect(api.createMemory).toHaveBeenCalledWith({
      boardId: 'board-1',
      title: 'New context',
      text: 'A durable note for this board.',
      status: 'statement',
    })
  })

  it('saves a correction with the server revision', async () => {
    const wrapper = mount(WorkspaceMemoryView)
    await settle()
    await wrapper.get('[data-action="edit-memory"]').trigger('click')
    await wrapper.get('#memory-text').setValue('Corrected context.')
    await wrapper.get('form').trigger('submit')
    await settle()

    expect(api.updateMemory).toHaveBeenCalledWith('memory-1', {
      title: memory.title,
      text: 'Corrected context.',
      status: memory.status,
      revision: memory.revision,
    })
  })

  it('archives the entry with its current revision', async () => {
    const wrapper = mount(WorkspaceMemoryView)
    await settle()
    await wrapper.get('[data-action="toggle-memory-archive"]').trigger('click')
    await settle()

    expect(api.setMemoryArchived).toHaveBeenCalledWith('memory-1', { archived: true, revision: memory.revision })
    expect(wrapper.text()).toContain('No memory yet')
  })

  it('keeps board mutation out of the memory copy', async () => {
    const wrapper = mount(WorkspaceMemoryView)
    await settle()
    expect(wrapper.text()).toContain('never changes board cards, columns, or statuses')
  })
})
