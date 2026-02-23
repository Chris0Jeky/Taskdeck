import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import CaptureModal from '../../components/common/CaptureModal.vue'

const mockCaptureStore = reactive({
  error: null as string | null,
  createItem: vi.fn<(dto: { boardId: string | null; text: string; source?: string | null }) => Promise<{ id: string }>>(),
})

vi.mock('../../store/captureStore', () => ({
  useCaptureStore: () => mockCaptureStore,
}))

async function waitForUi() {
  await Promise.resolve()
  await Promise.resolve()
}

describe('CaptureModal', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockCaptureStore.error = null
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

  it('closes on Escape key', async () => {
    const wrapper = mount(CaptureModal)

    await wrapper.get('.td-capture-modal').trigger('keydown', { key: 'Escape' })
    await waitForUi()

    expect(wrapper.emitted('close')).toHaveLength(1)
  })
})
