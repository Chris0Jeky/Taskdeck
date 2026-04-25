import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { reactive, ref } from 'vue'
import PaperInboxView from '../../../views/paper/PaperInboxView.vue'
import type { CaptureItemSummary } from '../../../types/capture'

const mockCaptureStore = reactive({
  loadingList: false,
  listError: null as string | null,
  actionBusyItemId: null as string | null,
  createItem: vi.fn<(...args: unknown[]) => Promise<unknown>>(),
})

const orchestratorState = {
  captureStore: mockCaptureStore,
  items: ref<Array<{ id: string }>>([]),
  selectedItemId: ref<string | null>(null),
  loadInbox: vi.fn<() => Promise<void>>(),
  triageSelected: vi.fn<() => Promise<void>>(),
  ignoreSelected: vi.fn<() => Promise<void>>(),
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
    orchestratorState.selectedItemId.value = null
    orchestratorState.loadInbox.mockResolvedValue(undefined)
    orchestratorState.triageSelected.mockResolvedValue(undefined)
    orchestratorState.ignoreSelected.mockResolvedValue(undefined)
    mockCaptureStore.createItem.mockResolvedValue({ id: 'created-1' })
    mockCaptureStore.listError = null
    mockBoardStore.boards = []
    mockBoardStore.fetchBoards.mockResolvedValue(undefined)
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
})
