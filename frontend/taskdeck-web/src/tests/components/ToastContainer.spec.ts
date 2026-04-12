import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import ToastContainer from '../../components/common/ToastContainer.vue'
import type { Toast } from '../../store/toastStore'

const mockToastStore = reactive({
  toasts: [] as Toast[],
  remove: vi.fn(),
})

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => mockToastStore,
}))

describe('ToastContainer', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockToastStore.toasts = []
  })

  it('renders nothing when there are no toasts', () => {
    const wrapper = mount(ToastContainer)
    // Container exists but no toast items
    expect(wrapper.findAll('[role="alert"]')).toHaveLength(0)
    expect(wrapper.text()).toBe('')
  })

  it('renders toast messages', () => {
    mockToastStore.toasts = [
      { id: 't1', message: 'Board created', type: 'success', duration: 3000 },
      { id: 't2', message: 'Network error', type: 'error', duration: 5000 },
    ]
    const wrapper = mount(ToastContainer)
    expect(wrapper.text()).toContain('Board created')
    expect(wrapper.text()).toContain('Network error')
  })

  it('applies error role="alert" for error toasts', () => {
    mockToastStore.toasts = [
      { id: 't1', message: 'Failed', type: 'error', duration: 5000 },
    ]
    const wrapper = mount(ToastContainer)
    const errorToast = wrapper.find('[role="alert"]')
    expect(errorToast.exists()).toBe(true)
    expect(errorToast.text()).toContain('Failed')
  })

  it('does not apply role="alert" for non-error toasts', () => {
    mockToastStore.toasts = [
      { id: 't1', message: 'Saved', type: 'success', duration: 3000 },
    ]
    const wrapper = mount(ToastContainer)
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
  })

  it('calls remove when close button is clicked', async () => {
    mockToastStore.toasts = [
      { id: 't1', message: 'Dismiss me', type: 'info', duration: 3000 },
    ]
    const wrapper = mount(ToastContainer)
    const closeBtn = wrapper.find('button[aria-label="Close"]')
    expect(closeBtn.exists()).toBe(true)
    await closeBtn.trigger('click')
    expect(mockToastStore.remove).toHaveBeenCalledWith('t1')
  })

  it('renders success toast with success styling', () => {
    mockToastStore.toasts = [
      { id: 't1', message: 'Done', type: 'success', duration: 3000 },
    ]
    const wrapper = mount(ToastContainer)
    // Success toast should have green-themed classes
    const toastEl = wrapper.find('.bg-green-50')
    expect(toastEl.exists()).toBe(true)
  })

  it('renders warning toast with warning styling', () => {
    mockToastStore.toasts = [
      { id: 't1', message: 'Warning', type: 'warning', duration: 4000 },
    ]
    const wrapper = mount(ToastContainer)
    const toastEl = wrapper.find('.bg-yellow-50')
    expect(toastEl.exists()).toBe(true)
  })

  it('has aria-live="polite" on the container', () => {
    const wrapper = mount(ToastContainer)
    expect(wrapper.find('[aria-live="polite"]').exists()).toBe(true)
  })
})
