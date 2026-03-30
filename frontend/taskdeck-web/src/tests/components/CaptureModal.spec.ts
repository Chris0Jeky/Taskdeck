import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import CaptureModal from '../../components/common/CaptureModal.vue'

const escapeHandlers: Array<() => void> = []
const unregisterEscapeHandlerMock = vi.fn()

const mockCaptureStore = reactive({
  actionError: null as string | null,
  createItem: vi.fn<(dto: { boardId: string | null; text: string; source?: string | null }) => Promise<{ id: string }>>(),
})

vi.mock('../../store/captureStore', () => ({
  useCaptureStore: () => mockCaptureStore,
}))

vi.mock('../../composables/useEscapeStack', () => ({
  registerEscapeHandler: vi.fn((handler: () => void) => {
    escapeHandlers.push(handler)
    return () => {
      const index = escapeHandlers.indexOf(handler)
      if (index >= 0) {
        escapeHandlers.splice(index, 1)
      }
      unregisterEscapeHandlerMock()
    }
  }),
}))

async function waitForUi() {
  await Promise.resolve()
  await Promise.resolve()
}

describe('CaptureModal', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    escapeHandlers.splice(0, escapeHandlers.length)
    mockCaptureStore.actionError = null
    mockCaptureStore.createItem.mockResolvedValue({ id: 'capture-created' })
  })

  it('submits with Ctrl+Enter and emits created + close', async () => {
    const wrapper = mount(CaptureModal)

    const textarea = wrapper.get('textarea')
    await textarea.setValue('Capture this task')
    await textarea.trigger('keydown', { key: 'Enter', ctrlKey: true })
    await waitForUi()

    expect(mockCaptureStore.createItem).toHaveBeenCalledWith({
      boardId: null,
      text: 'Capture this task',
      source: 'Typed',
    })

    expect(wrapper.emitted('created')).toEqual([['capture-created']])
    expect(wrapper.emitted('close')).toHaveLength(1)
  })

  it('validates empty text and does not submit', async () => {
    const wrapper = mount(CaptureModal)

    await wrapper.get('button.td-btn--primary').trigger('click')
    await waitForUi()

    expect(wrapper.text()).toContain('Capture text is required.')
    expect(mockCaptureStore.createItem).not.toHaveBeenCalled()
  })

  it('closes via escape stack handler', async () => {
    const wrapper = mount(CaptureModal)

    expect(escapeHandlers.length).toBeGreaterThan(0)
    escapeHandlers[escapeHandlers.length - 1]?.()
    await waitForUi()

    expect(wrapper.emitted('close')).toHaveLength(1)
  })

  it('prevents re-entrant submit while save is in flight', async () => {
    let resolveCreate: ((value: { id: string }) => void) | null = null
    mockCaptureStore.createItem.mockImplementation(() => new Promise((resolve) => {
      resolveCreate = resolve
    }))

    const wrapper = mount(CaptureModal)
    const textarea = wrapper.get('textarea')
    await textarea.setValue('Capture this task once')

    await textarea.trigger('keydown', { key: 'Enter', ctrlKey: true })
    await textarea.trigger('keydown', { key: 'Enter', ctrlKey: true })
    await waitForUi()

    expect(mockCaptureStore.createItem).toHaveBeenCalledTimes(1)
    expect(wrapper.emitted('close')).toBeUndefined()

    resolveCreate?.({ id: 'capture-created' })
    await waitForUi()

    expect(wrapper.emitted('close')).toHaveLength(1)
  })

  it('does not close with Escape while saving', async () => {
    mockCaptureStore.createItem.mockImplementation(
      () => new Promise(() => {
        // Intentionally unresolved to keep saving state active.
      }))

    const wrapper = mount(CaptureModal)
    const textarea = wrapper.get('textarea')
    await textarea.setValue('Slow capture')
    await textarea.trigger('keydown', { key: 'Enter', ctrlKey: true })
    await waitForUi()

    expect(escapeHandlers.length).toBeGreaterThan(0)
    escapeHandlers[escapeHandlers.length - 1]?.()
    await waitForUi()

    expect(wrapper.emitted('close')).toBeUndefined()
  })

  it('unregisters escape handler on unmount', async () => {
    const wrapper = mount(CaptureModal)
    await waitForUi()

    wrapper.unmount()

    expect(unregisterEscapeHandlerMock).toHaveBeenCalledTimes(1)
  })

  it('scopes captures to the provided board and surfaces the board hint', async () => {
    const wrapper = mount(CaptureModal, {
      props: {
        boardId: 'board-7',
        boardName: 'Support Board',
      },
    })

    await wrapper.get('textarea').setValue('Capture this for support')
    await wrapper.get('button.td-btn--primary').trigger('click')
    await waitForUi()

    expect(mockCaptureStore.createItem).toHaveBeenCalledWith({
      boardId: 'board-7',
      text: 'Capture this for support',
      source: 'Typed',
    })
    expect(wrapper.text()).toContain('This capture will stay linked to Support Board.')
  })

  describe('transcript capture mode', () => {
    it('renders mode tabs with Quick Capture active by default', () => {
      const wrapper = mount(CaptureModal)

      const tabs = wrapper.findAll('[role="tab"]')
      expect(tabs).toHaveLength(2)
      expect(tabs[0].text()).toBe('Quick Capture')
      expect(tabs[1].text()).toBe('Transcript')
      expect(tabs[0].attributes('aria-selected')).toBe('true')
      expect(tabs[1].attributes('aria-selected')).toBe('false')
    })

    it('switches to transcript mode when Transcript tab is clicked', async () => {
      const wrapper = mount(CaptureModal)

      const transcriptTab = wrapper.findAll('[role="tab"]')[1]
      await transcriptTab.trigger('click')
      await waitForUi()

      expect(transcriptTab.attributes('aria-selected')).toBe('true')
      expect(wrapper.text()).toContain('Paste a meeting transcript')
      expect(wrapper.find('.td-capture-modal__file-bar').exists()).toBe(true)
    })

    it('submits with TranscriptPaste source when text is pasted in transcript mode', async () => {
      const wrapper = mount(CaptureModal)

      const transcriptTab = wrapper.findAll('[role="tab"]')[1]
      await transcriptTab.trigger('click')
      await waitForUi()

      const textarea = wrapper.get('.td-capture-modal__input--transcript')
      await textarea.setValue('Speaker 1: Hello\nSpeaker 2: World')
      await wrapper.get('button.td-btn--primary').trigger('click')
      await waitForUi()

      expect(mockCaptureStore.createItem).toHaveBeenCalledWith({
        boardId: null,
        text: 'Speaker 1: Hello\nSpeaker 2: World',
        source: 'TranscriptPaste',
      })
    })

    it('shows character count in transcript mode', async () => {
      const wrapper = mount(CaptureModal)

      const transcriptTab = wrapper.findAll('[role="tab"]')[1]
      await transcriptTab.trigger('click')
      await waitForUi()

      const textarea = wrapper.get('.td-capture-modal__input--transcript')
      await textarea.setValue('Some transcript text')
      await waitForUi()

      expect(wrapper.find('.td-capture-modal__char-count').exists()).toBe(true)
      expect(wrapper.find('.td-capture-modal__char-count').text()).toContain('20')
      expect(wrapper.find('.td-capture-modal__char-count').text()).toContain('51,200')
    })

    it('does not show character count in typed mode', () => {
      const wrapper = mount(CaptureModal)

      expect(wrapper.find('.td-capture-modal__char-count').exists()).toBe(false)
    })

    it('shows Upload .txt file button in transcript mode', async () => {
      const wrapper = mount(CaptureModal)

      const transcriptTab = wrapper.findAll('[role="tab"]')[1]
      await transcriptTab.trigger('click')
      await waitForUi()

      const uploadBtn = wrapper.find('.td-capture-modal__file-bar .td-btn')
      expect(uploadBtn.exists()).toBe(true)
      expect(uploadBtn.text()).toBe('Upload .txt file')
    })

    it('validates empty text in transcript mode', async () => {
      const wrapper = mount(CaptureModal)

      const transcriptTab = wrapper.findAll('[role="tab"]')[1]
      await transcriptTab.trigger('click')
      await waitForUi()

      await wrapper.get('button.td-btn--primary').trigger('click')
      await waitForUi()

      expect(wrapper.text()).toContain('Capture text is required.')
      expect(mockCaptureStore.createItem).not.toHaveBeenCalled()
    })

    it('disables tab switching while saving', async () => {
      mockCaptureStore.createItem.mockImplementation(
        () => new Promise(() => {
          // Intentionally unresolved.
        }))

      const wrapper = mount(CaptureModal)
      const textarea = wrapper.get('textarea')
      await textarea.setValue('Saving...')
      await textarea.trigger('keydown', { key: 'Enter', ctrlKey: true })
      await waitForUi()

      const tabs = wrapper.findAll('[role="tab"]')
      expect(tabs[1].attributes('disabled')).toBeDefined()
    })

    it('preserves board scope in transcript mode', async () => {
      const wrapper = mount(CaptureModal, {
        props: {
          boardId: 'board-42',
          boardName: 'Dev Board',
        },
      })

      const transcriptTab = wrapper.findAll('[role="tab"]')[1]
      await transcriptTab.trigger('click')
      await waitForUi()

      expect(wrapper.text()).toContain('This capture will stay linked to Dev Board.')

      const textarea = wrapper.get('.td-capture-modal__input--transcript')
      await textarea.setValue('Transcript for dev board')
      await wrapper.get('button.td-btn--primary').trigger('click')
      await waitForUi()

      expect(mockCaptureStore.createItem).toHaveBeenCalledWith({
        boardId: 'board-42',
        text: 'Transcript for dev board',
        source: 'TranscriptPaste',
      })
    })

    it('clears inline error when switching modes', async () => {
      const wrapper = mount(CaptureModal)

      // Trigger validation error in typed mode
      await wrapper.get('button.td-btn--primary').trigger('click')
      await waitForUi()
      expect(wrapper.text()).toContain('Capture text is required.')

      // Switch to transcript mode
      const transcriptTab = wrapper.findAll('[role="tab"]')[1]
      await transcriptTab.trigger('click')
      await waitForUi()

      expect(wrapper.text()).not.toContain('Capture text is required.')
    })
  })
})
