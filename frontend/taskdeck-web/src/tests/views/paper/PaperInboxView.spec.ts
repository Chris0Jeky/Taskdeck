import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { enableAutoUnmount, flushPromises, mount, RouterLinkStub } from '@vue/test-utils'
import { reactive, ref } from 'vue'
import PaperInboxView from '../../../views/paper/PaperInboxView.vue'
import { i18n } from '../../../i18n'
import type { CaptureItem, CaptureItemSummary } from '../../../types/capture'
import MockAdapter from 'axios-mock-adapter'
import http from '../../../api/http'
import { AUTH_EXPIRED_EVENT } from '../../../utils/authExpiry'
import {
  CAPTURE_DRAFT_STORAGE_KEY,
  peekCaptureDraft,
  stashCaptureDraft,
} from '../../../utils/captureDraftStash'

const mockCaptureStore = reactive({
  loadingList: false,
  listError: null as string | null,
  actionBusyItemId: null as string | null,
  triagePollingItemId: null as string | null,
  createItem: vi.fn<(...args: unknown[]) => Promise<unknown>>(),
  triageItem: vi.fn<(...args: unknown[]) => Promise<unknown>>(),
  keepItem: vi.fn<(...args: unknown[]) => Promise<unknown>>(),
  archiveItem: vi.fn<(...args: unknown[]) => Promise<unknown>>(),
  ignoreItem: vi.fn<(...args: unknown[]) => Promise<unknown>>(),
  pollTriageCompletion: vi.fn<(...args: unknown[]) => () => void>(),
  // `peekDetail` is the read-only detail load (#1973): it returns the item
  // WITHOUT caching it into `detailById` or syncing the list summary, which is
  // what keeps archived inspection from disturbing live capture state.
  peekDetail: vi.fn<(...args: unknown[]) => Promise<CaptureItem>>(),
  fetchDetail: vi.fn<(...args: unknown[]) => Promise<CaptureItem>>(),
  detailById: {} as Record<string, { status: string } | undefined>,
})

const orchestratorState = {
  captureStore: mockCaptureStore,
  items: ref<Array<{ id: string }>>([]),
  activeBoardId: ref<string | null>(null),
  activeColumnId: ref<string | null>(null),
  isArchivedHistory: ref(false),
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

// The signed-in account drives the capture-draft stash's identity gate
// (GH-2142). Kept as a plain reactive stub so a spec can switch users the way
// a /login round trip does.
const mockSessionStore = reactive({ userId: 'user-a' as string | null })

vi.mock('../../../store/sessionStore', () => ({
  useSessionStore: () => mockSessionStore,
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

// A mounted view keeps a window listener for the auth-expiry notice (GH-2142),
// so a wrapper left mounted by one spec stashes a draft into the next one's
// expectations. Tear every wrapper down between specs.
enableAutoUnmount(afterEach)

describe('PaperInboxView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    orchestratorState.items.value = []
    orchestratorState.activeBoardId.value = null
    orchestratorState.activeColumnId.value = null
    orchestratorState.isArchivedHistory.value = false
    orchestratorState.activeBoardName.value = ''
    orchestratorState.activeColumnName.value = ''
    orchestratorState.selectedItemId.value = null
    orchestratorState.loadInbox.mockResolvedValue(undefined)
    orchestratorState.clearScope.mockResolvedValue(undefined)
    mockCaptureStore.createItem.mockResolvedValue({ id: 'created-1', metadata: null })
    mockCaptureStore.triageItem.mockResolvedValue({ status: 'Triaging', alreadyTriaging: false })
    mockCaptureStore.keepItem.mockResolvedValue(undefined)
    mockCaptureStore.archiveItem.mockResolvedValue(undefined)
    mockCaptureStore.ignoreItem.mockResolvedValue(undefined)
    mockCaptureStore.pollTriageCompletion.mockReturnValue(() => undefined)
    mockCaptureStore.peekDetail.mockReset()
    mockCaptureStore.fetchDetail.mockReset()
    mockCaptureStore.detailById = {}
    mockCaptureStore.listError = null
    mockCaptureStore.actionBusyItemId = null
    mockCaptureStore.triagePollingItemId = null
    mockBoardStore.boards = []
    mockBoardStore.fetchBoards.mockResolvedValue(undefined)
    mockSessionStore.userId = 'user-a'
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

  it('renders archived capture history read-only and blocks Paper capture and triage mutations', async () => {
    orchestratorState.isArchivedHistory.value = true
    orchestratorState.activeBoardId.value = 'board-archived'
    orchestratorState.activeBoardName.value = 'Archived board'
    orchestratorState.items.value = [captureRow('history-capture', 'New')]

    const wrapper = mount(PaperInboxView, { attachTo: document.body })

    expect(wrapper.attributes('data-history-mode')).toBe('archived')
    expect(wrapper.text()).toContain('Archived capture history')
    expect(wrapper.find('[data-testid="paper-inbox-capture"]').exists()).toBe(false)
    expect(wrapper.find('textarea[aria-label="Capture body"]').exists()).toBe(false)
    expect(wrapper.find('textarea[aria-label="Quick capture input"]').exists()).toBe(false)
    expect(wrapper.find('[data-action="accept"]').exists()).toBe(false)
    expect(wrapper.find('[data-action="reject"]').exists()).toBe(false)
    expect(wrapper.find('[data-action="edit"]').exists()).toBe(false)

    const table = wrapper.findComponent({ name: 'PaperTriageTable' })
    expect(table.props('readOnly')).toBe(true)
    table.vm.$emit('accept', 'history-capture', 'board-archived')
    table.vm.$emit('reject', 'history-capture')
    await flushPromises()

    expect(mockCaptureStore.createItem).not.toHaveBeenCalled()
    expect(mockCaptureStore.triageItem).not.toHaveBeenCalled()
    expect(mockCaptureStore.keepItem).not.toHaveBeenCalled()
    expect(mockCaptureStore.archiveItem).not.toHaveBeenCalled()
    expect(mockCaptureStore.ignoreItem).not.toHaveBeenCalled()

    window.dispatchEvent(new KeyboardEvent('keydown', { key: ';', metaKey: true }))
    await wrapper.vm.$nextTick()
    expect(wrapper.attributes('data-variant')).toBe('composer')
    wrapper.unmount()
  })

  // Regression for the second half of #1973's read-only boundary. Stripping the
  // triage controls left an archived row with nothing but a truncated
  // `textExcerpt` and an open button that did nothing — the retained capture was
  // "reachable" in name only, which is the disclosure defect the issue is about.
  it('expands a read-only detail surface for a retained archived capture', async () => {
    orchestratorState.isArchivedHistory.value = true
    orchestratorState.activeBoardId.value = 'board-archived'
    orchestratorState.activeBoardName.value = 'Archived board'
    orchestratorState.items.value = [captureRow('history-capture', 'ProposalCreated')]
    mockCaptureStore.peekDetail.mockResolvedValue({
      ...captureRow('history-capture', 'ProposalCreated'),
      boardId: 'board-archived',
      rawText: 'The full retained capture text the excerpt truncated.',
      retryCount: 0,
      provenance: {
        captureItemId: 'history-capture',
        triageRunId: 'run-9',
        proposalId: 'proposal-9',
        promptVersion: 'prompt-v3',
      },
    } as CaptureItem)

    const wrapper = mount(PaperInboxView, {
      global: { stubs: { RouterLink: RouterLinkStub } },
    })
    expect(wrapper.find('[data-testid="capture-history-detail"]').exists()).toBe(false)

    await wrapper.find('[data-testid="capture-history-open"]').trigger('click')
    await flushPromises()

    const detail = wrapper.find('[data-testid="capture-history-detail"]')
    expect(detail.exists()).toBe(true)
    expect(detail.find('[data-testid="capture-history-text"]').text()).toBe(
      'The full retained capture text the excerpt truncated.',
    )
    expect(detail.text()).toContain('run-9')
    expect(detail.text()).toContain('prompt-v3')

    // The decision record stays inside archived history, not the live queue.
    const link = wrapper.findComponent(RouterLinkStub)
    expect(link.props('to')).toBe(
      '/workspace/review?boardId=board-archived&history=archived#proposal-proposal-9',
    )

    // Inspection reads and nothing else: the non-caching peek, never the
    // summary-syncing fetch, and no triage/ignore call.
    expect(mockCaptureStore.peekDetail).toHaveBeenCalledWith('history-capture', {
      recordError: false,
      showToast: false,
    })
    expect(mockCaptureStore.fetchDetail).not.toHaveBeenCalled()
    expect(mockCaptureStore.triageItem).not.toHaveBeenCalled()
    expect(mockCaptureStore.ignoreItem).not.toHaveBeenCalled()

    // Toggling closed collapses it again.
    await wrapper.find('[data-testid="capture-history-open"]').trigger('click')
    await flushPromises()
    expect(wrapper.find('[data-testid="capture-history-detail"]').exists()).toBe(false)
    wrapper.unmount()
  })

  it('states that no decision record exists when triage recorded no proposal', async () => {
    orchestratorState.isArchivedHistory.value = true
    orchestratorState.activeBoardId.value = 'board-archived'
    orchestratorState.items.value = [captureRow('history-capture', 'Ignored')]
    mockCaptureStore.peekDetail.mockResolvedValue({
      ...captureRow('history-capture', 'Ignored'),
      boardId: 'board-archived',
      rawText: 'An ignored capture that never became a proposal.',
      retryCount: 0,
      provenance: null,
    } as CaptureItem)

    const wrapper = mount(PaperInboxView, {
      global: { stubs: { RouterLink: RouterLinkStub } },
    })
    await wrapper.find('[data-testid="capture-history-open"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-testid="capture-history-proposal-link"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="capture-history-no-proposal"]').exists()).toBe(true)
    wrapper.unmount()
  })

  it('surfaces a retained-capture load failure instead of an empty panel', async () => {
    orchestratorState.isArchivedHistory.value = true
    orchestratorState.activeBoardId.value = 'board-archived'
    orchestratorState.items.value = [captureRow('history-capture', 'ProposalCreated')]
    mockCaptureStore.peekDetail.mockRejectedValue(new Error('boom'))

    const wrapper = mount(PaperInboxView, {
      global: { stubs: { RouterLink: RouterLinkStub } },
    })
    await wrapper.find('[data-testid="capture-history-open"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-testid="capture-history-detail-error"]').exists()).toBe(true)
    wrapper.unmount()
  })

  it('leaves the open affordance inert outside archived history', async () => {
    orchestratorState.items.value = [captureRow('live-capture', 'New')]

    const wrapper = mount(PaperInboxView)
    expect(wrapper.find('[data-testid="capture-history-open"]').exists()).toBe(false)

    const table = wrapper.findComponent({ name: 'PaperTriageTable' })
    table.vm.$emit('open', 'live-capture')
    await flushPromises()

    expect(mockCaptureStore.peekDetail).not.toHaveBeenCalled()
    wrapper.unmount()
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

  it('surfaces inspectable request diagnostics and associates the nib receipt on failure', async () => {
    // GH-1938: the receipt must carry the status + client correlation id so the
    // failure is reportable, and the nib input must point at it for assistive
    // tech instead of relying on a toast that expires.
    mockCaptureStore.createItem.mockRejectedValueOnce({
      response: {
        status: 503,
        data: { errorCode: 'UnexpectedError', message: 'Capture service unavailable' },
      },
      config: {
        method: 'post',
        url: '/capture/items',
        headers: { 'X-Request-Id': 'req-inbox-1938' },
      },
    })
    const wrapper = mount(PaperInboxView)
    ;(wrapper.vm as unknown as { setVariant: (next: 'nib' | 'composer') => void }).setVariant('nib')
    await wrapper.vm.$nextTick()

    const textarea = wrapper.find('textarea[aria-label="Quick capture input"]')
    await textarea.setValue('Do not lose this quick note')
    await textarea.trigger('keydown', { key: 'Enter' })
    await flushPromises()
    await wrapper.vm.$nextTick()

    const receipt = wrapper.get('[data-testid="paper-inbox-capture-error"]')
    expect(receipt.attributes('id')).toBe('paper-inbox-capture-error')

    const diagnostics = wrapper.get('[data-testid="paper-inbox-capture-error-diagnostics"]')
    expect(diagnostics.text()).toContain('Status: 503')
    expect(diagnostics.text()).toContain('req-inbox-1938')

    const nib = wrapper.find('textarea[aria-label="Quick capture input"]')
    expect(nib.attributes('aria-invalid')).toBe('true')
    expect(nib.attributes('aria-describedby')).toBe('paper-inbox-capture-error')
  })

  it('scopes the capture receipt to the variant that failed and drops it on toggle', async () => {
    // GH-1938: `captureError` was one shared ref, so a nib failure's receipt
    // stayed rendered under the composer after a `⌘;` toggle. Each variant now
    // owns its own receipt.
    mockCaptureStore.createItem.mockRejectedValueOnce(new Error('offline'))
    const wrapper = mount(PaperInboxView)
    const setVariant = (wrapper.vm as unknown as {
      setVariant: (next: 'nib' | 'composer') => void
    }).setVariant
    setVariant('nib')
    await wrapper.vm.$nextTick()

    const textarea = wrapper.find('textarea[aria-label="Quick capture input"]')
    await textarea.setValue('Nib note')
    await textarea.trigger('keydown', { key: 'Enter' })
    await flushPromises()
    expect(wrapper.find('[data-testid="paper-inbox-capture-error"]').exists()).toBe(true)

    setVariant('composer')
    await wrapper.vm.$nextTick()
    expect(wrapper.find('[data-testid="paper-inbox-capture-error"]').exists()).toBe(false)
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

  it('calls captureStore.archiveItem by ID without mutating selectedItemId', async () => {
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

    expect(mockCaptureStore.archiveItem).toHaveBeenCalledWith('capture-reject')
    expect(orchestratorState.selectedItemId.value).toBeNull()
  })

  it('keeps a capture for later without starting triage', async () => {
    orchestratorState.items.value = [captureRow('capture-keep', 'New')]

    const wrapper = mount(PaperInboxView)
    await wrapper.find('[data-action="keep"]').trigger('click')
    await flushPromises()

    expect(mockCaptureStore.keepItem).toHaveBeenCalledWith('capture-keep')
    expect(mockCaptureStore.triageItem).not.toHaveBeenCalled()
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

  // ── 401 draft survival (GH-2142) ───────────────────────────────────────
  //
  // The 401 handler in api/http.ts hard-navigates to /login, destroying the
  // retained draft AND its failure receipt. These specs cover the stash-then-
  // restore round trip that makes the capture survive that journey.
  describe('capture draft across the 401 redirect', () => {
    beforeEach(() => {
      window.sessionStorage.clear()
    })

    afterEach(() => {
      window.sessionStorage.clear()
    })

    function expireSession() {
      window.dispatchEvent(new CustomEvent(AUTH_EXPIRED_EVENT))
    }

    it('stashes the composer draft with its metadata when the session expires', async () => {
      mockBoardStore.boards = [{ id: 'board-9', name: 'Ops' }]
      const wrapper = mount(PaperInboxView)
      await flushPromises()

      await wrapper.find('textarea[aria-label="Capture body"]').setValue('Do not lose this')
      await wrapper.find('select[aria-label="Board picker"]').setValue('board-9')
      await wrapper.find('input[aria-label="Add label"]').setValue('ops')
      await wrapper.find('input[aria-label="Add label"]').trigger('keydown', { key: 'Enter' })
      await wrapper.find('input[aria-label="Due date"]').setValue('2026-09-02')

      expireSession()

      const stashed = peekCaptureDraft(mockSessionStore.userId)
      expect(stashed).toMatchObject({
        variant: 'composer',
        text: 'Do not lose this',
        boardId: 'board-9',
        labels: ['ops'],
        dueAt: '2026-09-02',
      })
      // The redirect usually beats the request's own rejection, so the receipt
      // is synthesised rather than left empty.
      expect(stashed?.failure?.message).toBe('Your session expired before this capture was saved.')
    })

    it('carries an existing failure receipt into the stash instead of overwriting it', async () => {
      mockCaptureStore.createItem.mockRejectedValueOnce({
        response: { status: 401, data: { errorCode: 'AuthenticationFailed', message: 'Token expired' } },
        config: { method: 'post', url: '/capture' },
      })
      const wrapper = mount(PaperInboxView)
      const textarea = wrapper.find('textarea[aria-label="Capture body"]')
      await textarea.setValue('Receipt keeper')
      await textarea.trigger('keydown', { key: 'Enter', metaKey: true })
      await flushPromises()

      expireSession()

      const stashed = peekCaptureDraft(mockSessionStore.userId)
      expect(stashed?.failure?.message).toBe('Token expired')
      expect(stashed?.failure?.details).toContain('Status: 401')
    })

    it('stashes nothing when the draft is empty', async () => {
      mount(PaperInboxView)
      await flushPromises()

      expireSession()

      expect(window.sessionStorage.getItem(CAPTURE_DRAFT_STORAGE_KEY)).toBeNull()
    })

    it('does not stash from archived history, which has no capture surface', async () => {
      orchestratorState.isArchivedHistory.value = true
      mount(PaperInboxView)
      await flushPromises()

      expireSession()

      expect(window.sessionStorage.getItem(CAPTURE_DRAFT_STORAGE_KEY)).toBeNull()
    })

    it('restores the stashed composer draft, its receipt, and an explicit affordance', async () => {
      mockBoardStore.boards = [{ id: 'board-9', name: 'Ops' }]
      stashCaptureDraft({
        userId: 'user-a',
        variant: 'composer',
        text: 'Survived the redirect',
        boardId: 'board-9',
        labels: ['ops'],
        dueAt: '2026-09-02',
        failure: { message: 'Your session expired before this capture was saved.', details: null },
      })

      const wrapper = mount(PaperInboxView)
      await flushPromises()

      const textarea = wrapper.find<HTMLTextAreaElement>('textarea[aria-label="Capture body"]')
      expect(textarea.element.value).toBe('Survived the redirect')
      expect(
        wrapper.find<HTMLSelectElement>('select[aria-label="Board picker"]').element.value,
      ).toBe('board-9')
      expect(wrapper.find<HTMLInputElement>('input[aria-label="Due date"]').element.value).toBe(
        '2026-09-02',
      )
      expect(wrapper.text()).toContain('ops')

      const notice = wrapper.get('[data-testid="paper-inbox-capture-restored"]')
      expect(notice.attributes('role')).toBe('status')
      expect(notice.text()).toContain('Draft restored.')

      expect(wrapper.get('[data-testid="paper-inbox-capture-error"]').text()).toContain(
        'Your session expired before this capture was saved.',
      )

      // Single use: a later reload must not resurrect a second copy.
      expect(window.sessionStorage.getItem(CAPTURE_DRAFT_STORAGE_KEY)).toBeNull()
    })

    it('restores a nib stash onto the nib surface', async () => {
      stashCaptureDraft({ userId: 'user-a', variant: 'nib', text: 'Quick note that survived' })

      const wrapper = mount(PaperInboxView)
      await flushPromises()

      expect(wrapper.attributes('data-variant')).toBe('nib')
      expect(
        wrapper.find<HTMLTextAreaElement>('textarea[aria-label="Quick capture input"]').element.value,
      ).toBe('Quick note that survived')
    })

    it('leaves the stash alone when the Inbox opens in archived history', async () => {
      orchestratorState.isArchivedHistory.value = true
      stashCaptureDraft({ userId: 'user-a', variant: 'composer', text: 'Wait for a live inbox' })

      mount(PaperInboxView)
      await flushPromises()

      expect(peekCaptureDraft(mockSessionStore.userId)?.text).toBe('Wait for a live inbox')
    })

    it('discarding the restored draft empties the surface, the receipt, and the affordance', async () => {
      stashCaptureDraft({
        userId: 'user-a',
        variant: 'composer',
        text: 'Not wanted after all',
        failure: { message: 'Your session expired before this capture was saved.', details: null },
      })

      const wrapper = mount(PaperInboxView)
      await flushPromises()
      // The session expired again before the restored draft was re-sent, so a
      // fresh stash exists at discard time; discarding must take it with it.
      expireSession()
      expect(window.sessionStorage.getItem(CAPTURE_DRAFT_STORAGE_KEY)).not.toBeNull()

      await wrapper.get('[data-testid="paper-inbox-capture-restored-discard"]').trigger('click')
      await flushPromises()

      expect(
        wrapper.find<HTMLTextAreaElement>('textarea[aria-label="Capture body"]').element.value,
      ).toBe('')
      expect(wrapper.find('[data-testid="paper-inbox-capture-restored"]').exists()).toBe(false)
      expect(wrapper.find('[data-testid="paper-inbox-capture-error"]').exists()).toBe(false)
      expect(window.sessionStorage.getItem(CAPTURE_DRAFT_STORAGE_KEY)).toBeNull()
    })

    it('clears the stash once a capture is saved', async () => {
      const wrapper = mount(PaperInboxView)
      await flushPromises()
      // A stash left behind by an earlier interrupted attempt in this tab.
      stashCaptureDraft({ userId: 'user-a', variant: 'composer', text: 'Stale interrupted attempt' })

      const textarea = wrapper.find('textarea[aria-label="Capture body"]')
      await textarea.setValue('Saved for real')
      await textarea.trigger('keydown', { key: 'Enter', metaKey: true })
      await flushPromises()

      expect(mockCaptureStore.createItem).toHaveBeenCalledTimes(1)
      expect(window.sessionStorage.getItem(CAPTURE_DRAFT_STORAGE_KEY)).toBeNull()
      expect(wrapper.find('[data-testid="paper-inbox-capture-restored"]').exists()).toBe(false)
    })

    it('survives a real 401 on capture submit: redirect, sign in again, draft back in the composer', async () => {
      // The whole journey through the production 401 path (GH-2142 AC-1/AC-2):
      // the capture POST 401s, api/http.ts clears the session and assigns
      // location.href, and the draft has to come back on the other side.
      const originalLocation = window.location
      Object.defineProperty(window, 'location', {
        value: { pathname: '/workspace/inbox', search: '', href: '' },
        writable: true,
        configurable: true,
      })
      const adapter = new MockAdapter(http)
      adapter.onPost('/capture').reply(401, { message: 'Token expired' })
      mockCaptureStore.createItem.mockImplementationOnce(async () => {
        await http.post('/capture', { text: 'Survives a real 401' })
        return { id: 'never' }
      })
      mockBoardStore.boards = [{ id: 'board-9', name: 'Ops' }]

      const before = mount(PaperInboxView)
      await flushPromises()
      const textarea = before.find('textarea[aria-label="Capture body"]')
      await textarea.setValue('Survives a real 401')
      await before.find('select[aria-label="Board picker"]').setValue('board-9')
      await textarea.trigger('keydown', { key: 'Enter', metaKey: true })
      await flushPromises()

      // The interceptor really did navigate...
      expect(window.location.href).toBe(
        '/login?redirect=' + encodeURIComponent('/workspace/inbox'),
      )
      // ...and the document teardown that navigation implies loses the view.
      before.unmount()
      adapter.restore()
      Object.defineProperty(window, 'location', {
        value: originalLocation,
        writable: true,
        configurable: true,
      })

      // Back on the Inbox after signing in again.
      const after = mount(PaperInboxView)
      await flushPromises()

      expect(
        after.find<HTMLTextAreaElement>('textarea[aria-label="Capture body"]').element.value,
      ).toBe('Survives a real 401')
      expect(
        after.find<HTMLSelectElement>('select[aria-label="Board picker"]').element.value,
      ).toBe('board-9')
      expect(after.get('[data-testid="paper-inbox-capture-restored"]').text()).toContain(
        'Draft restored.',
      )
      // The interceptor fires before the POST's own rejection reaches the
      // view, so the receipt is the synthesised session-expiry reason rather
      // than the server message - an explanation either way, never a blank.
      expect(after.get('[data-testid="paper-inbox-capture-error"]').text()).toContain(
        'Your session expired before this capture was saved.',
      )

    })

    it('restores a stash only to the account that made it, and clears it otherwise', async () => {
      // GH-2142 review M1: the same tab, a different sign-in. B must not
      // receive A's draft -- dispatchCapture would post it under B's session.
      stashCaptureDraft({ userId: 'user-a', variant: 'composer', text: "A's private thought" })
      mockSessionStore.userId = 'user-b'

      const wrapper = mount(PaperInboxView)
      await flushPromises()

      expect(
        wrapper.find<HTMLTextAreaElement>('textarea[aria-label="Capture body"]').element.value,
      ).toBe('')
      expect(wrapper.find('[data-testid="paper-inbox-capture-restored"]').exists()).toBe(false)
      expect(window.sessionStorage.getItem(CAPTURE_DRAFT_STORAGE_KEY)).toBeNull()
      wrapper.unmount()

      // And the same record does reach its owner.
      stashCaptureDraft({ userId: 'user-a', variant: 'composer', text: "A's private thought" })
      mockSessionStore.userId = 'user-a'
      const owner = mount(PaperInboxView)
      await flushPromises()

      expect(
        owner.find<HTMLTextAreaElement>('textarea[aria-label="Capture body"]').element.value,
      ).toBe("A's private thought")
      owner.unmount()
    })

    it('stashes nothing when no account is signed in', async () => {
      mockSessionStore.userId = null
      const wrapper = mount(PaperInboxView)
      await flushPromises()
      await wrapper.find('textarea[aria-label="Capture body"]').setValue('Unowned draft')

      expireSession()

      expect(window.sessionStorage.getItem(CAPTURE_DRAFT_STORAGE_KEY)).toBeNull()
      wrapper.unmount()
    })

    it('stashes a nib draft the user typed then toggled away from', async () => {
      // Review M2: both surfaces stay mounted under v-show, so the variant on
      // screen is not necessarily the one holding text.
      const wrapper = mount(PaperInboxView)
      await flushPromises()
      ;(wrapper.vm as unknown as { setVariant: (next: 'nib' | 'composer') => void }).setVariant('nib')
      await wrapper.vm.$nextTick()
      await wrapper.find('textarea[aria-label="Quick capture input"]').setValue('Typed in the nib')
      ;(wrapper.vm as unknown as { setVariant: (next: 'nib' | 'composer') => void }).setVariant(
        'composer',
      )
      await wrapper.vm.$nextTick()

      expireSession()

      expect(peekCaptureDraft(mockSessionStore.userId)).toMatchObject({
        variant: 'nib',
        text: 'Typed in the nib',
      })
      wrapper.unmount()
    })

    it('prefers the surface on screen when both hold text', async () => {
      const wrapper = mount(PaperInboxView)
      await flushPromises()
      ;(wrapper.vm as unknown as { setVariant: (next: 'nib' | 'composer') => void }).setVariant('nib')
      await wrapper.vm.$nextTick()
      await wrapper.find('textarea[aria-label="Quick capture input"]').setValue('Nib text')
      ;(wrapper.vm as unknown as { setVariant: (next: 'nib' | 'composer') => void }).setVariant(
        'composer',
      )
      await wrapper.vm.$nextTick()
      await wrapper.find('textarea[aria-label="Capture body"]').setValue('Composer text')

      expireSession()

      expect(peekCaptureDraft(mockSessionStore.userId)).toMatchObject({
        variant: 'composer',
        text: 'Composer text',
      })
      wrapper.unmount()
    })

    it('stashes nothing when the session expires during the post-save refresh', async () => {
      // The capture IS saved; only the follow-up refresh 401s. Re-stashing the
      // submitted text would invite a duplicate submit after signing in.
      orchestratorState.loadInbox.mockImplementationOnce(async () => {
        expireSession()
        throw new Error('Unauthorized')
      })
      const wrapper = mount(PaperInboxView)
      await flushPromises()
      const textarea = wrapper.find('textarea[aria-label="Capture body"]')
      await textarea.setValue('Already saved once')
      await textarea.trigger('keydown', { key: 'Enter', metaKey: true })
      await flushPromises()

      expect(mockCaptureStore.createItem).toHaveBeenCalledTimes(1)
      expect(window.sessionStorage.getItem(CAPTURE_DRAFT_STORAGE_KEY)).toBeNull()
      expect(
        wrapper.find<HTMLTextAreaElement>('textarea[aria-label="Capture body"]').element.value,
      ).toBe('')
    })

    it('leaves no auth-expiry listener behind when the view is unmounted', async () => {
      const removeSpy = vi.spyOn(window, 'removeEventListener')
      const wrapper = mount(PaperInboxView)
      await flushPromises()
      await wrapper.find('textarea[aria-label="Capture body"]').setValue('Gone with the view')

      wrapper.unmount()

      expect(removeSpy).toHaveBeenCalledWith(AUTH_EXPIRED_EVENT, expect.any(Function))
      removeSpy.mockRestore()

      expireSession()
      expect(window.sessionStorage.getItem(CAPTURE_DRAFT_STORAGE_KEY)).toBeNull()
    })
  })
})
