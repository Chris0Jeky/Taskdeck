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
})
