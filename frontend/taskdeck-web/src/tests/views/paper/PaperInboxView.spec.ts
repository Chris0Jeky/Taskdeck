import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { reactive, ref } from 'vue'
import PaperInboxView from '../../../views/paper/PaperInboxView.vue'
import type { CaptureItemSummary } from '../../../types/capture'

const mockCaptureStore = reactive({
  loadingList: false,
  listError: null as string | null,
  actionBusyItemId: null as string | null,
  triagePollingItemId: null as string | null,
  createItem: vi.fn<(...args: unknown[]) => Promise<unknown>>(),
  triageItem: vi.fn<(...args: unknown[]) => Promise<unknown>>(),
  ignoreItem: vi.fn<(...args: unknown[]) => Promise<unknown>>(),
  pollTriageCompletion: vi.fn<(...args: unknown[]) => () => void>(),
  detailById: {} as Record<string, { status: string } | undefined>,
})

const orchestratorState = {
  captureStore: mockCaptureStore,
  items: ref<Array<{ id: string }>>([]),
  activeBoardId: ref<string | null>(null),
  selectedItemId: ref<string | null>(null),
  loadInbox: vi.fn<() => Promise<void>>(),
}

vi.mock('../../../composables/useInboxOrchestrator', () => ({
  useInboxOrchestrator: () => orchestratorState,
}))

const mockBoardStore = reactive({
  boards: [] as Array<{ id: string; name: string }>,
  fetchBoards: vi.fn<() => Promise<void>>(),
})

vi.mock('../../../store/boardStore', () => ({
  useBoardStore: () => mockBoardStore,
}))

describe('PaperInboxView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    orchestratorState.items.value = []
    orchestratorState.activeBoardId.value = null
    orchestratorState.selectedItemId.value = null
    orchestratorState.loadInbox.mockResolvedValue(undefined)
    mockCaptureStore.createItem.mockResolvedValue({ id: 'created-1' })
    mockCaptureStore.triageItem.mockResolvedValue({ status: 'Triaging', alreadyTriaging: false })
    mockCaptureStore.ignoreItem.mockResolvedValue(undefined)
    mockCaptureStore.pollTriageCompletion.mockReturnValue(() => undefined)
    mockCaptureStore.detailById = {}
    mockCaptureStore.listError = null
    mockCaptureStore.actionBusyItemId = null
    mockCaptureStore.triagePollingItemId = null
    mockBoardStore.boards = []
    mockBoardStore.fetchBoards.mockResolvedValue(undefined)
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('defaults to the composer variant', () => {
    const wrapper = mount(PaperInboxView)
    // Composer renders a textarea with an aria label "Capture body".
    expect(wrapper.find('textarea[aria-label="Capture body"]').exists()).toBe(true)
    expect(wrapper.attributes('data-variant')).toBe('composer')
  })

  it('toggles between composer and nib when Cmd+; is pressed globally', async () => {
    const wrapper = mount(PaperInboxView, { attachTo: document.body })
    expect(wrapper.attributes('data-variant')).toBe('composer')

    // Dispatch the chord on window — the view registers a global listener.
    const downA = new KeyboardEvent('keydown', { key: ';', metaKey: true, cancelable: true })
    window.dispatchEvent(downA)
    await wrapper.vm.$nextTick()
    expect(wrapper.attributes('data-variant')).toBe('nib')

    const downB = new KeyboardEvent('keydown', { key: ';', metaKey: true, cancelable: true })
    window.dispatchEvent(downB)
    await wrapper.vm.$nextTick()
    expect(wrapper.attributes('data-variant')).toBe('composer')

    wrapper.unmount()
  })

  it('toggles via Ctrl+; on non-Mac', async () => {
    const wrapper = mount(PaperInboxView, { attachTo: document.body })
    const ev = new KeyboardEvent('keydown', { key: ';', ctrlKey: true, cancelable: true })
    window.dispatchEvent(ev)
    await wrapper.vm.$nextTick()
    expect(wrapper.attributes('data-variant')).toBe('nib')
    wrapper.unmount()
  })

  it('removes its global listener on unmount', async () => {
    const wrapper = mount(PaperInboxView, { attachTo: document.body })
    wrapper.unmount()
    // After unmount, dispatching the chord must not throw and must not
    // mutate any orphan reactive state — we simply confirm the dispatch is
    // safe and noop-y.
    const ev = new KeyboardEvent('keydown', { key: ';', metaKey: true, cancelable: true })
    expect(() => window.dispatchEvent(ev)).not.toThrow()
  })

  it('switches via the Nib / Composer hairline toggles', async () => {
    const wrapper = mount(PaperInboxView)
    const buttons = wrapper.findAll('button')
    const nibBtn = buttons.find((b) => b.text().includes('Nib'))
    expect(nibBtn).toBeDefined()
    await nibBtn!.trigger('click')
    expect(wrapper.attributes('data-variant')).toBe('nib')
  })

  it('preserves composer and nib drafts while switching capture variants', async () => {
    const wrapper = mount(PaperInboxView)
    const setVariant = (wrapper.vm as unknown as { setVariant: (next: 'nib' | 'composer') => void }).setVariant
    const composer = wrapper.find<HTMLTextAreaElement>('textarea[aria-label="Capture body"]')
    await composer.setValue('Composer draft')

    setVariant('nib')
    await wrapper.vm.$nextTick()
    const nib = wrapper.find<HTMLTextAreaElement>('textarea[aria-label="Quick capture input"]')
    await nib.setValue('Nib draft')

    setVariant('composer')
    await wrapper.vm.$nextTick()
    expect(composer.element.value).toBe('Composer draft')

    setVariant('nib')
    await wrapper.vm.$nextTick()
    expect(nib.element.value).toBe('Nib draft')
  })

  it('moves focus to the newly active capture variant when toggled', async () => {
    const wrapper = mount(PaperInboxView, { attachTo: document.body })
    const setVariant = (wrapper.vm as unknown as { setVariant: (next: 'nib' | 'composer') => void }).setVariant

    setVariant('nib')
    await wrapper.vm.$nextTick()
    expect(document.activeElement).toBe(wrapper.find('textarea[aria-label="Quick capture input"]').element)

    setVariant('composer')
    await wrapper.vm.$nextTick()
    expect(document.activeElement).toBe(wrapper.find('textarea[aria-label="Capture body"]').element)

    wrapper.unmount()
  })

  it('resets the composer draft after capture creation succeeds', async () => {
    const wrapper = mount(PaperInboxView)
    const textarea = wrapper.find('textarea[aria-label="Capture body"]')
    await textarea.setValue('Ship the inbox fix')
    await textarea.trigger('keydown', { key: 'Enter', metaKey: true })
    await flushPromises()

    expect(mockCaptureStore.createItem).toHaveBeenCalledWith({
      boardId: null,
      text: 'Ship the inbox fix',
      source: 'Typed',
    })
    expect((textarea.element as HTMLTextAreaElement).value).toBe('')
  })

  it('defaults composer captures to the active board', async () => {
    orchestratorState.activeBoardId.value = 'board-active'
    mockBoardStore.boards = [{ id: 'board-active', name: 'Active board' }]

    const wrapper = mount(PaperInboxView)
    await flushPromises()
    const textarea = wrapper.find('textarea[aria-label="Capture body"]')
    await textarea.setValue('Capture in board context')
    await textarea.trigger('keydown', { key: 'Enter', metaKey: true })
    await flushPromises()

    expect(mockCaptureStore.createItem).toHaveBeenCalledWith({
      boardId: 'board-active',
      text: 'Capture in board context',
      source: 'Typed',
    })
  })

  it('lets composer captures explicitly land outside the active board', async () => {
    orchestratorState.activeBoardId.value = 'board-active'
    mockBoardStore.boards = [{ id: 'board-active', name: 'Active board' }]

    const wrapper = mount(PaperInboxView)
    await flushPromises()
    await wrapper.find('select[aria-label="Board picker"]').setValue('')
    const textarea = wrapper.find('textarea[aria-label="Capture body"]')
    await textarea.setValue('Capture without board context')
    await textarea.trigger('keydown', { key: 'Enter', metaKey: true })
    await flushPromises()

    expect(mockCaptureStore.createItem).toHaveBeenCalledWith({
      boardId: null,
      text: 'Capture without board context',
      source: 'Typed',
    })
  })

  it('defaults nib captures to the active board', async () => {
    orchestratorState.activeBoardId.value = 'board-active'

    const wrapper = mount(PaperInboxView)
    ;(wrapper.vm as unknown as { setVariant: (next: 'nib' | 'composer') => void }).setVariant('nib')
    await wrapper.vm.$nextTick()

    const textarea = wrapper.find('textarea[aria-label="Quick capture input"]')
    await textarea.setValue('Quick note in board context')
    await textarea.trigger('keydown', { key: 'Enter' })
    await flushPromises()

    expect(mockCaptureStore.createItem).toHaveBeenCalledWith({
      boardId: 'board-active',
      text: 'Quick note in board context',
      source: 'Typed',
    })
  })

  it('preserves the composer draft when capture creation fails', async () => {
    mockCaptureStore.createItem.mockRejectedValueOnce(new Error('offline'))
    const wrapper = mount(PaperInboxView)
    const textarea = wrapper.find('textarea[aria-label="Capture body"]')
    await textarea.setValue('Do not lose this draft')
    await textarea.trigger('keydown', { key: 'Enter', metaKey: true })
    await flushPromises()

    expect((textarea.element as HTMLTextAreaElement).value).toBe('Do not lose this draft')
  })

  it('guards composer submissions while capture creation is in flight', async () => {
    let resolveCreate: (value: unknown) => void = () => undefined
    mockCaptureStore.createItem.mockReturnValueOnce(new Promise((resolve) => {
      resolveCreate = resolve
    }))

    const wrapper = mount(PaperInboxView)
    const textarea = wrapper.find('textarea[aria-label="Capture body"]')
    await textarea.setValue('Submit this once')
    await textarea.trigger('keydown', { key: 'Enter', metaKey: true })
    await textarea.trigger('keydown', { key: 'Enter', metaKey: true })

    expect(mockCaptureStore.createItem).toHaveBeenCalledTimes(1)
    resolveCreate({ id: 'created-1' })
    await flushPromises()
  })

  it('preserves the nib draft when capture creation fails', async () => {
    mockCaptureStore.createItem.mockRejectedValueOnce(new Error('offline'))
    const wrapper = mount(PaperInboxView)
    ;(wrapper.vm as unknown as { setVariant: (next: 'nib' | 'composer') => void }).setVariant('nib')
    await wrapper.vm.$nextTick()

    const textarea = wrapper.find('textarea[aria-label="Quick capture input"]')
    await textarea.setValue('Do not lose this quick note')
    await textarea.trigger('keydown', { key: 'Enter' })
    await flushPromises()

    expect((textarea.element as HTMLTextAreaElement).value).toBe('Do not lose this quick note')
  })

  it('guards nib submissions while capture creation is in flight', async () => {
    let resolveCreate: (value: unknown) => void = () => undefined
    mockCaptureStore.createItem.mockReturnValueOnce(new Promise((resolve) => {
      resolveCreate = resolve
    }))

    const wrapper = mount(PaperInboxView)
    ;(wrapper.vm as unknown as { setVariant: (next: 'nib' | 'composer') => void }).setVariant('nib')
    await wrapper.vm.$nextTick()

    const textarea = wrapper.find('textarea[aria-label="Quick capture input"]')
    await textarea.setValue('Submit this nib once')
    await textarea.trigger('keydown', { key: 'Enter' })
    await textarea.trigger('keydown', { key: 'Enter' })

    expect(mockCaptureStore.createItem).toHaveBeenCalledTimes(1)
    resolveCreate({ id: 'created-1' })
    await flushPromises()
  })

  it('refocuses the nib input after the bleed placeholder clears', async () => {
    vi.useFakeTimers()
    const wrapper = mount(PaperInboxView, { attachTo: document.body })
    ;(wrapper.vm as unknown as { setVariant: (next: 'nib' | 'composer') => void }).setVariant('nib')
    await wrapper.vm.$nextTick()

    const textarea = wrapper.find<HTMLTextAreaElement>('textarea[aria-label="Quick capture input"]')
    await textarea.setValue('Capture and keep typing')
    await textarea.trigger('keydown', { key: 'Enter' })
    await flushPromises()

    expect(wrapper.find('[data-testid="paper-nib-bleed"]').exists()).toBe(true)

    await vi.advanceTimersByTimeAsync(1400)
    await wrapper.vm.$nextTick()

    expect(document.activeElement).toBe(
      wrapper.find<HTMLTextAreaElement>('textarea[aria-label="Quick capture input"]').element,
    )
    wrapper.unmount()
  })

  it('does not mutate selected item when opening a paper row', async () => {
    orchestratorState.selectedItemId.value = 'polling-capture'
    orchestratorState.items.value = [
      {
        id: 'capture-1',
        userId: 'u-1',
        boardId: null,
        status: 'New',
        source: 'Typed',
        textExcerpt: 'Keep polling stable',
        createdAt: new Date().toISOString(),
        processedAt: null,
      },
    ] as CaptureItemSummary[]

    const wrapper = mount(PaperInboxView)
    await wrapper.find('.paper-triage__open').trigger('click')

    expect(orchestratorState.selectedItemId.value).toBe('polling-capture')
  })

  it('calls captureStore.triageItem by ID without mutating selectedItemId', async () => {
    orchestratorState.selectedItemId.value = null
    orchestratorState.items.value = [
      {
        id: 'capture-triage',
        userId: 'u-1',
        boardId: 'board-x',
        status: 'New',
        source: 'Typed',
        textExcerpt: 'Triage me',
        createdAt: new Date().toISOString(),
        processedAt: null,
      },
    ] as CaptureItemSummary[]

    const wrapper = mount(PaperInboxView)
    await wrapper.find('[data-action="accept"]').trigger('click')
    await flushPromises()

    expect(mockCaptureStore.triageItem).toHaveBeenCalledWith('capture-triage', 'board-x')
    expect(orchestratorState.selectedItemId.value).toBeNull()
  })

  it('requires a board before triaging a board-less capture, then triages the chosen board (#1764)', async () => {
    mockBoardStore.boards = [{ id: 'board-alpha', name: 'Alpha' }]
    orchestratorState.selectedItemId.value = null
    orchestratorState.items.value = [
      {
        id: 'capture-boardless',
        userId: 'u-1',
        boardId: null,
        status: 'New',
        source: 'Typed',
        textExcerpt: 'No board yet',
        createdAt: new Date().toISOString(),
        processedAt: null,
      },
    ] as CaptureItemSummary[]

    const wrapper = mount(PaperInboxView)
    // Accept opens the picker instead of triaging immediately.
    await wrapper.find('[data-action="accept"]').trigger('click')
    await flushPromises()
    expect(mockCaptureStore.triageItem).not.toHaveBeenCalled()

    // Choose a board and confirm — now triage runs with the chosen board.
    await wrapper.find('[data-testid="capture-board-pick"] select').setValue('board-alpha')
    await wrapper.find('[data-action="accept-on-board"]').trigger('click')
    await flushPromises()

    expect(mockCaptureStore.triageItem).toHaveBeenCalledWith('capture-boardless', 'board-alpha')
  })

  it('calls captureStore.ignoreItem by ID without mutating selectedItemId', async () => {
    orchestratorState.selectedItemId.value = null
    orchestratorState.items.value = [
      {
        id: 'capture-reject',
        userId: 'u-1',
        boardId: null,
        status: 'New',
        source: 'Typed',
        textExcerpt: 'Reject me',
        createdAt: new Date().toISOString(),
        processedAt: null,
      },
    ] as CaptureItemSummary[]

    const wrapper = mount(PaperInboxView)
    await wrapper.find('[data-action="reject"]').trigger('click')
    await flushPromises()

    expect(mockCaptureStore.ignoreItem).toHaveBeenCalledWith('capture-reject')
    expect(orchestratorState.selectedItemId.value).toBeNull()
  })

  it('starts triage polling when triageItem resolves with non-terminal status', async () => {
    mockCaptureStore.triageItem.mockResolvedValue({ status: 'Triaging', alreadyTriaging: false })
    orchestratorState.items.value = [
      {
        id: 'capture-poll',
        userId: 'u-1',
        boardId: 'board-x',
        status: 'New',
        source: 'Typed',
        textExcerpt: 'Poll me',
        createdAt: new Date().toISOString(),
        processedAt: null,
      },
    ] as CaptureItemSummary[]

    const wrapper = mount(PaperInboxView)
    await wrapper.find('[data-action="accept"]').trigger('click')
    await flushPromises()

    expect(mockCaptureStore.pollTriageCompletion).toHaveBeenCalledWith('capture-poll')
  })

  it('skips triage polling when detail shows terminal status', async () => {
    mockCaptureStore.triageItem.mockResolvedValue({ status: 'Triaged', alreadyTriaging: false })
    mockCaptureStore.detailById = { 'capture-done': { status: 'Triaged' } }
    orchestratorState.items.value = [
      {
        id: 'capture-done',
        userId: 'u-1',
        boardId: 'board-x',
        status: 'New',
        source: 'Typed',
        textExcerpt: 'Already done',
        createdAt: new Date().toISOString(),
        processedAt: null,
      },
    ] as CaptureItemSummary[]

    const wrapper = mount(PaperInboxView)
    await wrapper.find('[data-action="accept"]').trigger('click')
    await flushPromises()

    expect(mockCaptureStore.pollTriageCompletion).not.toHaveBeenCalled()
  })

  it('preserves existing selectedItemId during triage accept', async () => {
    orchestratorState.selectedItemId.value = 'other-item'
    orchestratorState.items.value = [
      {
        id: 'capture-accept',
        userId: 'u-1',
        boardId: 'board-x',
        status: 'New',
        source: 'Typed',
        textExcerpt: 'Accept me',
        createdAt: new Date().toISOString(),
        processedAt: null,
      },
    ] as CaptureItemSummary[]

    const wrapper = mount(PaperInboxView)
    await wrapper.find('[data-action="accept"]').trigger('click')
    await flushPromises()

    expect(orchestratorState.selectedItemId.value).toBe('other-item')
  })
})
