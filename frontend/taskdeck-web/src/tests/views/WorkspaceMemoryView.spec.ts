import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { enableAutoUnmount, flushPromises, mount } from '@vue/test-utils'
import { reactive } from 'vue'
import WorkspaceMemoryView from '../../views/WorkspaceMemoryView.vue'
import type { Board } from '../../types/board'
import type { Memory } from '../../types/workspaceInsights'

enableAutoUnmount(afterEach)

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
  onBeforeRouteLeave: vi.fn(),
  onBeforeRouteUpdate: vi.fn(),
  useRoute: () => routeMock,
  RouterLink: {
    props: ['to'],
    template: '<a :href="typeof to === \'string\' ? to : \'#\'"><slot /></a>',
  },
}))

vi.mock('../../store/boardStore', () => ({
  useBoardStore: () => boardStore,
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
  it.each(['Close', 'Cancel'])('keeps a new private draft until %s is explicitly confirmed', async (action) => {
    const wrapper = mount(WorkspaceMemoryView, { global: { stubs: { Teleport: true } } })
    await settle()
    const button = (name: string) => wrapper.findAll('button').find(item => item.text() === name)!
    await wrapper.get('[data-action="new-memory"]').trigger('click')
    await wrapper.get('#memory-title').setValue('An unfinished thought')
    await wrapper.get('#memory-text').setValue('Keep these exact words until I decide.')
    await button(action).trigger('click')
    expect(wrapper.get('[role="dialog"]').text()).toContain('Discard memory draft?')
    await button('Keep editing').trigger('click')
    expect((wrapper.get('#memory-text').element as HTMLTextAreaElement).value).toBe('Keep these exact words until I decide.')
    await button(action).trigger('click')
    await button('Discard draft').trigger('click')
    expect(wrapper.find('form').exists()).toBe(false)
    expect(api.createMemory).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('closes an unchanged existing memory without a discard prompt', async () => {
    const wrapper = mount(WorkspaceMemoryView, { global: { stubs: { Teleport: true } } })
    await settle()
    await wrapper.get('[data-action="edit-memory"]').trigger('click')
    await wrapper.findAll('button').find(item => item.text() === 'Close')!.trigger('click')
    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
    expect(wrapper.find('form').exists()).toBe(false)
    wrapper.unmount()
  })

  beforeEach(() => {
    vi.resetAllMocks()
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

  it('shows an API error message when memory loading fails', async () => {
    api.getMemories.mockRejectedValueOnce({ response: { data: { message: 'Memory service unavailable' } } })
    const wrapper = mount(WorkspaceMemoryView)
    await settle()

    expect(wrapper.find('[role="alert"]').text()).toContain('Memory service unavailable')
    wrapper.unmount()
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

  it('waits for the initial list before allowing a new memory', async () => {
    let finish!: (value: Memory[]) => void
    api.getMemories.mockReturnValueOnce(new Promise<Memory[]>(resolve => { finish = resolve }))
    const wrapper = mount(WorkspaceMemoryView)
    await settle()
    expect(wrapper.get('[data-action="new-memory"]').attributes('disabled')).toBeDefined()
    await wrapper.get('[data-action="new-memory"]').trigger('click')
    expect(wrapper.find('form').exists()).toBe(false)
    finish([memory])
    await settle()
    expect(wrapper.get('[data-action="new-memory"]').attributes('disabled')).toBeUndefined()
    wrapper.unmount()
  })

  it('retries board discovery after a linked-board transition fails', async () => {
    const wrapper = mount(WorkspaceMemoryView)
    await settle()
    boardStore.boards = [board, { ...board, id: 'board-2' }]
    boardStore.fetchBoards.mockRejectedValueOnce(new Error('Board discovery unavailable'))
    routeMock.query = { boardId: 'board-2' }
    await settle()
    expect(wrapper.find('[role="alert"]').text()).toContain('Board discovery unavailable')
    await wrapper.find('[role="alert"] button').trigger('click')
    await settle()
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
    expect(api.getMemories).toHaveBeenLastCalledWith('board-2', false)
    wrapper.unmount()
  })

  it('keeps board mutation out of the memory copy', async () => {
    const wrapper = mount(WorkspaceMemoryView)
    await settle()
    expect(wrapper.text()).toContain('never changes board cards, columns, or statuses')
  })
})
