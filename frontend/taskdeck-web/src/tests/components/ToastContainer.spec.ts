import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { nextTick, reactive } from 'vue'
import ToastContainer from '../../components/common/ToastContainer.vue'
import type { Toast } from '../../store/toastStore'
import { i18n, type SupportedLocale } from '../../i18n'

const mockToastStore = reactive({
  toasts: [] as Toast[],
  remove: vi.fn(),
})
const copyToastReceipt = vi.hoisted(() => vi.fn().mockResolvedValue(true))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => mockToastStore,
  copyToastReceipt,
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
    expect(wrapper.get('[data-toast-id="t1"]').text()).toContain('Board created')
    expect(wrapper.get('[data-toast-id="t2"]').text()).toContain('Network error')
  })

  it('keeps toasts present at mount and remount out of the polite announcement', async () => {
    mockToastStore.toasts = [
      { id: 'existing', message: 'Already visible', type: 'success', duration: 0 },
    ]

    const firstWrapper = mount(ToastContainer)
    await nextTick()
    expect(firstWrapper.get('[data-toast-polite-announcer]').text()).toBe('')

    firstWrapper.unmount()
    const remountedWrapper = mount(ToastContainer)
    await nextTick()
    expect(remountedWrapper.get('[data-toast-polite-announcer]').text()).toBe('')
    remountedWrapper.unmount()
  })

  it('announces only a non-error toast added after mount', async () => {
    const wrapper = mount(ToastContainer)
    const announcer = wrapper.get('[data-toast-polite-announcer]')

    mockToastStore.toasts = [
      { id: 'new-success', message: 'Capture saved to inbox', type: 'success', duration: 0 },
      { id: 'new-error', message: 'Network error', type: 'error', duration: 0 },
    ]
    await nextTick()
    await flushPromises()
    await nextTick()

    expect(announcer.text()).toBe('Capture saved to inbox')
    const errorToast = wrapper.get('[role="alert"]')
    expect(errorToast.attributes('aria-live')).toBe('assertive')
    expect(errorToast.attributes('aria-atomic')).toBe('true')
    expect(wrapper.get('.bg-green-50').attributes('role')).toBeUndefined()
    wrapper.unmount()
  })

  it('keeps error cards assertive while visible non-error cards stay out of live regions', () => {
    mockToastStore.toasts = [
      { id: 't1', message: 'Failed', type: 'error', duration: 5000 },
      { id: 't2', message: 'Saved', type: 'success', duration: 3000 },
    ]
    const wrapper = mount(ToastContainer)
    const errorToast = wrapper.find('[role="alert"]')
    expect(errorToast.exists()).toBe(true)
    expect(errorToast.text()).toContain('Failed')
    expect(errorToast.attributes('aria-live')).toBe('assertive')
    expect(errorToast.attributes('aria-atomic')).toBe('true')

    const visibleStatusToast = wrapper.get('.bg-green-50')
    expect(visibleStatusToast.attributes('role')).toBeUndefined()
    expect(visibleStatusToast.attributes('aria-live')).toBeUndefined()
    expect(visibleStatusToast.attributes('aria-atomic')).toBeUndefined()
  })

  it('primes an empty polite region before announcing a newly added non-error toast', async () => {
    const wrapper = mount(ToastContainer)
    const announcer = wrapper.get('[data-toast-polite-announcer]')

    expect(announcer.attributes('role')).toBe('status')
    expect(announcer.text()).toBe('')

    mockToastStore.toasts = [
      { id: 't1', message: 'Capture saved to inbox', type: 'success', duration: 3000 },
    ]
    await nextTick()
    await flushPromises()
    await nextTick()

    expect(wrapper.get('.bg-green-50').text()).toContain('Capture saved to inbox')
    expect(announcer.text()).toBe('Capture saved to inbox')
  })

  it('calls remove when close button is clicked', async () => {
    mockToastStore.toasts = [
      { id: 't1', message: 'Dismiss me', type: 'info', duration: 3000 },
    ]
    const wrapper = mount(ToastContainer)
    const closeBtn = wrapper.find('button[aria-label="Dismiss notification"]')
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

  it('associates an error disclosure only while its details are mounted', async () => {
    mockToastStore.toasts = [
      {
        id: 't1',
        message: 'Network error',
        details: 'status: 503',
        type: 'error',
        duration: 0,
      },
    ]
    const wrapper = mount(ToastContainer)

    const detailsButton = wrapper.get('button[aria-expanded="false"]')
    expect(detailsButton.attributes('aria-expanded')).toBe('false')
    expect(detailsButton.attributes('aria-controls')).toBeUndefined()
    await detailsButton.trigger('click')
    expect(detailsButton.attributes('aria-expanded')).toBe('true')
    expect(detailsButton.attributes('aria-controls')).toBe('toast-details-t1')
    const details = wrapper.get('#toast-details-t1')
    expect(details.text()).toContain('status: 503')
    expect(details.attributes('aria-label')).toBe('Error details for Network error')

    await detailsButton.trigger('click')
    expect(detailsButton.attributes('aria-expanded')).toBe('false')
    expect(detailsButton.attributes('aria-controls')).toBeUndefined()
    expect(wrapper.find('#toast-details-t1').exists()).toBe(false)

    const copyButton = wrapper.findAll('button').find((button) => button.text() === 'Copy details')
    expect(copyButton).toBeDefined()
    await copyButton!.trigger('click')
    expect(copyToastReceipt).toHaveBeenCalledWith(mockToastStore.toasts[0])

    mockToastStore.toasts = []
    await nextTick()
    expect(wrapper.find('button[aria-expanded]').exists()).toBe(false)
  })

  it.each([
    ['en', 'Show details', 'Hide details', 'Copy details', 'Copied', 'Copy failed', 'Dismiss notification'],
    ['it', 'Mostra dettagli', 'Nascondi dettagli', 'Copia dettagli', 'Copiato', 'Copia non riuscita', 'Chiudi la notifica'],
    ['es', 'Mostrar detalles', 'Ocultar detalles', 'Copiar detalles', 'Copiado', 'No se pudo copiar', 'Cerrar la notificación'],
  ] as Array<[SupportedLocale, string, string, string, string, string, string]>)(
    'localizes persistent error receipt controls in %s',
    async (locale, show, hide, copy, copied, copyFailed, dismiss) => {
      i18n.global.locale.value = locale
      mockToastStore.toasts = [
        {
          id: `toast-${locale}`,
          message: 'Network error',
          details: 'status: 503',
          type: 'error',
          duration: 0,
        },
      ]
      const wrapper = mount(ToastContainer)

      const detailsButton = wrapper.get('button[aria-expanded="false"]')
      expect(detailsButton.text()).toBe(show)
      const copyButton = wrapper.findAll('button').find((button) => button.text() === copy)
      expect(copyButton).toBeDefined()
      expect(wrapper.get(`button[aria-label="${dismiss}"]`).attributes('aria-label')).toBe(dismiss)

      await detailsButton.trigger('click')
      expect(detailsButton.text()).toBe(hide)

      copyToastReceipt.mockResolvedValueOnce(true)
      await copyButton!.trigger('click')
      expect(copyButton!.text()).toBe(copied)

      copyToastReceipt.mockResolvedValueOnce(false)
      await copyButton!.trigger('click')
      expect(copyButton!.text()).toBe(copyFailed)
    },
  )
})
