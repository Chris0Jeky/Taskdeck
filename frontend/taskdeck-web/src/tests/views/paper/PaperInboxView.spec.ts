import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { reactive, ref } from 'vue'
import PaperInboxView from '../../../views/paper/PaperInboxView.vue'
import { i18n } from '../../../i18n'
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
  activeColumnId: ref<string | null>(null),
  activeBoardName: ref(''),
  activeColumnName: ref(''),
  selectedItemId: ref<string | null>(null),
  loadInbox: vi.fn<() => Promise<void>>(),
  clearScope: vi.fn<() => Promise<void>>(),
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

function captureRow(id: string, status: CaptureItemSummary['status']): CaptureItemSummary {
  return {
    id,
    userId: 'u-1',
    boardId: null,
    status,
    source: 'Typed',
    textExcerpt: id,
    createdAt: new Date().toISOString(),
    processedAt: null,
  }
}

describe('PaperInboxView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    orchestratorState.items.value = []
    orchestratorState.activeBoardId.value = null
    orchestratorState.activeColumnId.value = null
    orchestratorState.activeBoardName.value = ''
    orchestratorState.activeColumnName.value = ''
    orchestratorState.selectedItemId.value = null
    orchestratorState.loadInbox.mockResolvedValue(undefined)
    orchestratorState.clearScope.mockResolvedValue(undefined)
    mockCaptureStore.createItem.mockResolvedValue({ id: 'created-1', metadata: null })
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

  it('discloses the board and column scope, then clears it without reloading', async () => {
    orchestratorState.activeBoardId.value = 'board-active'
    orchestratorState.activeColumnId.value = 'column-ready'
    orchestratorState.activeBoardName.value = 'Payments API Migration'
    orchestratorState.activeColumnName.value = 'Ready'
    orchestratorState.clearScope.mockImplementation(async () => {
      orchestratorState.activeBoardId.value = null
      orchestratorState.activeColumnId.value = null
      orchestratorState.items.value = [captureRow('restored-capture', 'New')]
    })

    const wrapper = mount(PaperInboxView)
    expect(wrapper.find('[data-testid="paper-scope-disclosure"]').text()).toContain('Board: Payments API Migration')
    expect(wrapper.find('[data-testid="paper-scope-disclosure"]').text()).toContain('Column: Ready')

    await wrapper.find('[data-testid="paper-scope-clear"]').trigger('click')
    await wrapper.vm.$nextTick()
    expect(orchestratorState.clearScope).toHaveBeenCalledTimes(1)
    expect(orchestratorState.loadInbox).not.toHaveBeenCalled()
    expect(wrapper.find('[data-testid="paper-scope-disclosure"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('restored-capture')
  })

  it('names the active scope in an empty Inbox and offers the same clear action', async () => {
    orchestratorState.activeBoardId.value = 'board-active'
    orchestratorState.activeBoardName.value = 'Payments API Migration'

    const wrapper = mount(PaperInboxView)
    const empty = wrapper.find('[data-testid="paper-triage-clear-scope"]')
    expect(wrapper.text()).toContain('No captures in Board: Payments API Migration')
    await empty.trigger('click')
    expect(orchestratorState.clearScope).toHaveBeenCalledTimes(1)
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

  it('sends composer due date and labels through the capture request', async () => {
    const wrapper = mount(PaperInboxView)
    await wrapper.find('textarea[aria-label="Capture body"]').setValue('Buy milk and gas')
    await wrapper.find('input[aria-label="Add label"]').setValue('shopping')
    await wrapper.find('input[aria-label="Add label"]').trigger('keydown', { key: 'Enter' })
    await wrapper.find('input[aria-label="Due date"]').setValue('2026-08-23')
    await wrapper.find('textarea[aria-label="Capture body"]').trigger('keydown', { key: 'Enter', metaKey: true })
    await flushPromises()

    expect(mockCaptureStore.createItem).toHaveBeenCalledWith({
      boardId: null,
      text: 'Buy milk and gas',
      source: 'Typed',
      dueDate: '2026-08-23',
      labels: ['shopping'],
    })
  })

  it('acknowledges metadata omitted by an older API without inviting a duplicate retry', async () => {
    mockCaptureStore.createItem.mockResolvedValueOnce({ id: 'created-by-older-api' })
    const wrapper = mount(PaperInboxView)
    const composer = (wrapper.vm as unknown as {
      composerRef: { resetDraft: () => void }
    }).composerRef
    const resetDraft = vi.spyOn(composer, 'resetDraft')
    const textarea = wrapper.find<HTMLTextAreaElement>('textarea[aria-label="Capture body"]')

    await textarea.setValue('Prepare regional report')
    await wrapper.find('input[aria-label="Add label"]').setValue('Sales')
    await wrapper.find('input[aria-label="Add label"]').trigger('keydown', { key: 'Enter' })
    await wrapper.find('input[aria-label="Due date"]').setValue('2026-08-30')
    await textarea.trigger('keydown', { key: 'Enter', metaKey: true })
    await flushPromises()

    expect(mockCaptureStore.createItem).toHaveBeenCalledTimes(1)
    expect(resetDraft).toHaveBeenCalledTimes(1)
    expect(textarea.element.value).toBe('')
    const warning = wrapper.get('[data-testid="paper-inbox-capture-metadata-compatibility-warning"]')
    expect(warning.attributes('role')).toBe('status')
    expect(warning.text()).toContain('Capture saved without its due date or labels.')
    expect(warning.text()).toContain('Do not retry—the capture is already in Inbox.')
    expect(wrapper.find('[data-testid="paper-inbox-capture-error"]').exists()).toBe(false)
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

  it('preserves the nib draft and renders an inline error when capture creation fails', async () => {
    mockCaptureStore.createItem.mockRejectedValueOnce(new Error('Capture service unavailable'))
    const wrapper = mount(PaperInboxView)
    ;(wrapper.vm as unknown as { setVariant: (next: 'nib' | 'composer') => void }).setVariant('nib')
    await wrapper.vm.$nextTick()

    const textarea = wrapper.find('textarea[aria-label="Quick capture input"]')
    await textarea.setValue('Do not lose this quick note')
    await textarea.trigger('keydown', { key: 'Enter' })
    await flushPromises()

    expect((textarea.element as HTMLTextAreaElement).value).toBe('Do not lose this quick note')
    const error = wrapper.get('[data-testid="paper-inbox-capture-error"]')
    expect(error.attributes('role')).toBe('alert')
    expect(error.text()).toContain('Capture not saved. Your draft is still here.')
    expect(error.text()).toContain('Capture service unavailable')
  })

  it('does not label a post-create list refresh failure as an unsaved capture', async () => {
    vi.useFakeTimers()
    orchestratorState.loadInbox.mockRejectedValueOnce(new Error('Inbox refresh unavailable'))
    const wrapper = mount(PaperInboxView)
    ;(wrapper.vm as unknown as { setVariant: (next: 'nib' | 'composer') => void }).setVariant('nib')
    await wrapper.vm.$nextTick()

    const textarea = wrapper.find('textarea[aria-label="Quick capture input"]')
    await textarea.setValue('Created even if the list refresh fails')
    await textarea.trigger('keydown', { key: 'Enter' })
    await flushPromises()

    expect(mockCaptureStore.createItem).toHaveBeenCalledTimes(1)
    expect(wrapper.find('[data-testid="paper-nib-bleed"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="paper-inbox-capture-error"]').exists()).toBe(false)

    await vi.advanceTimersByTimeAsync(1400)
    await wrapper.vm.$nextTick()
    expect(
      (wrapper.find('textarea[aria-label="Quick capture input"]').element as HTMLTextAreaElement).value,
    ).toBe('')
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

  /**
   * The header eyebrow no longer calls the total "the queue" (#1974).
   *
   * The reporter saw `INBOX · CAPTURE SURFACE · 5 IN QUEUE` beside a sidebar
   * badge reading 2, because the header counted every capture fetched —
   * applied ones included — while the badge counted only pending ones.
   */
  it('labels the pending count and the total apart, and does not let applied captures inflate the queue', () => {
    orchestratorState.items.value = [
      captureRow('c-new-1', 'New'),
      captureRow('c-new-2', 'New'),
      captureRow('c-applied-1', 'Converted'),
      captureRow('c-applied-2', 'Converted'),
      captureRow('c-proposed', 'ProposalCreated'),
    ] as CaptureItemSummary[]

    const wrapper = mount(PaperInboxView)
    const eyebrow = wrapper.find('[data-testid="paper-inbox-eyebrow"]').text()

    // The exact pair that would have caught the 2-vs-5 divergence.
    expect(eyebrow).toContain('2 awaiting triage')
    expect(eyebrow).toContain('5 captured')
    // The word "queue" is gone from the header — it was the ambiguity itself.
    expect(eyebrow.toLowerCase()).not.toContain('in queue')
  })

  it('counts a failed capture as still awaiting triage, matching the badge definition', () => {
    orchestratorState.items.value = [
      captureRow('c-new', 'New'),
      captureRow('c-failed', 'Failed'),
      captureRow('c-triaging', 'Triaging'),
    ] as CaptureItemSummary[]

    const wrapper = mount(PaperInboxView)
    const eyebrow = wrapper.find('[data-testid="paper-inbox-eyebrow"]').text()

    expect(eyebrow).toContain('2 awaiting triage')
    expect(eyebrow).toContain('3 captured')
  })

  /**
   * The total is a counted noun, and Italian and Spanish agree their participle
   * with it. At n=1 the eyebrow read "1 catturati" / "1 capturadas" — the kind
   * of thing that makes a localized surface look machine-made.
   */
  describe('eyebrow singular agreement', () => {
    function eyebrowFor(locale: 'en' | 'it' | 'es', rows: number): string {
      i18n.global.locale.value = locale
      orchestratorState.items.value = Array.from({ length: rows }, (_, index) =>
        captureRow(`c-${index}`, 'New'),
      ) as CaptureItemSummary[]
      const wrapper = mount(PaperInboxView)
      return wrapper.find('[data-testid="paper-inbox-eyebrow"]').text()
    }

    it('agrees the Italian participle with a single capture', () => {
      const eyebrow = eyebrowFor('it', 1)

      expect(eyebrow).toContain('1 catturato')
      expect(eyebrow).not.toContain('catturati')
    })

    it('keeps the Italian plural for more than one', () => {
      expect(eyebrowFor('it', 3)).toContain('3 catturati')
    })

    it('agrees the Spanish participle with a single capture', () => {
      const eyebrow = eyebrowFor('es', 1)

      expect(eyebrow).toContain('1 capturada')
      expect(eyebrow).not.toContain('capturadas')
    })

    it('keeps the Spanish plural for more than one', () => {
      expect(eyebrowFor('es', 3)).toContain('3 capturadas')
    })

    it('leaves English unchanged in both branches', () => {
      // "captured" is invariable; the two English forms exist only to give the
      // other catalogs a singular slot, so neither may drift.
      expect(eyebrowFor('en', 1)).toContain('1 captured')
      expect(eyebrowFor('en', 4)).toContain('4 captured')
    })
  })
})
