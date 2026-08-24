import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount, type VueWrapper, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import PaperToastContainer from '../../../components/paper/PaperToastContainer.vue'
import { useToastStore, type Toast, type ToastLabel } from '../../../store/toastStore'
import { i18n, type SupportedLocale } from '../../../i18n'

describe('PaperToastContainer', () => {
  let wrapper: VueWrapper | null = null

  beforeEach(() => {
    setActivePinia(createPinia())
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
    wrapper?.unmount()
    wrapper = null
    Reflect.deleteProperty(navigator, 'clipboard')
    Reflect.deleteProperty(document, 'execCommand')
  })

  it('renders multiple toasts from the store', async () => {
    const store = useToastStore()
    store.show('First message', 'success', 0)
    store.show('Second message', 'info', 0)

    wrapper = mount(PaperToastContainer)
    await nextTick()

    const cards = wrapper.findAll('.paper-toast')
    expect(cards.length).toBe(2)
    const messages = cards.map((c) => c.find('.paper-toast__msg').text())
    // Stack reverses order so newest is on top.
    expect(messages).toContain('First message')
    expect(messages).toContain('Second message')
  })

  it('pauses the countdown on hover and resumes on leave', async () => {
    const store = useToastStore()
    // duration > 0 wires the auto-remove setTimeout; we use fake timers.
    store.show('Pausable', 'info', 4000)

    wrapper = mount(PaperToastContainer)
    await nextTick()

    const card = wrapper.find('.paper-toast')
    expect(card.exists()).toBe(true)

    function progressOf(): number {
      const bar = card.find('.paper-toast__bar').element as HTMLElement
      return Number(bar.style.getPropertyValue('--p'))
    }

    // Advance ~halfway, capture progress under hover (uses the float `--p`
    // CSS variable so we don't lose precision to the rounded "Ns" countdown).
    vi.advanceTimersByTime(1000)
    await nextTick()
    const beforeHover = progressOf()

    await card.trigger('mouseenter')
    // While paused, advancing timers should not move the displayed progress.
    vi.advanceTimersByTime(1500)
    await nextTick()
    const afterHover = progressOf()
    expect(afterHover).toBeCloseTo(beforeHover, 2)
    expect(store.toasts).toHaveLength(1)

    await card.trigger('mouseleave')
    // After resume, the displayed progress should drop again.
    vi.advanceTimersByTime(500)
    await nextTick()
    const afterResume = progressOf()
    expect(afterResume).toBeLessThan(beforeHover)
    expect(store.toasts).toHaveLength(1)

    vi.advanceTimersByTime(2500)
    await nextTick()
    expect(store.toasts).toHaveLength(0)
  })

  it('keeps the countdown paused while the toast still has focus', async () => {
    const store = useToastStore()
    store.show('Focusable', 'success', 4000)

    wrapper = mount(PaperToastContainer)
    await nextTick()

    const card = wrapper.find('.paper-toast')
    expect(card.exists()).toBe(true)

    function progressOf(): number {
      const bar = card.find('.paper-toast__bar').element as HTMLElement
      return Number(bar.style.getPropertyValue('--p'))
    }

    vi.advanceTimersByTime(1000)
    await nextTick()
    const beforeFocus = progressOf()

    await card.trigger('mouseenter')
    await card.trigger('focusin')
    await card.trigger('mouseleave')
    vi.advanceTimersByTime(1500)
    await nextTick()

    expect(store.toasts).toHaveLength(1)
    expect(progressOf()).toBeCloseTo(beforeFocus, 2)

    card.element.dispatchEvent(new FocusEvent('focusout', { bubbles: true, relatedTarget: document.body }))
    await nextTick()

    vi.advanceTimersByTime(500)
    await nextTick()
    expect(progressOf()).toBeLessThan(beforeFocus)
  })

  it('runs the action handler and emits action when the undo link is clicked', async () => {
    const store = useToastStore()
    const handler = vi.fn()
    const id = store.show('3 cards applied', 'success', 0, {
      title: '3 cards applied',
      action: { label: 'undo', hint: '6h', handler },
    })

    wrapper = mount(PaperToastContainer)
    await nextTick()

    const undoBtn = wrapper.find('.paper-toast__undo')
    expect(undoBtn.exists()).toBe(true)
    expect(undoBtn.text()).toContain('undo')
    expect(undoBtn.text()).toContain('6h')

    await undoBtn.trigger('click')
    await flushPromises()

    expect(handler).toHaveBeenCalledTimes(1)
    expect(wrapper.emitted('action')?.[0]).toEqual([id])
    // Toast is removed from the store after action.
    expect(store.toasts.find((t) => t.id === id)).toBeUndefined()
  })

  it('associates persistent error details only while mounted and announces by variant', async () => {
    const store = useToastStore()
    const id = store.error('Network error', undefined, { details: 'status: 503' })

    wrapper = mount(PaperToastContainer)
    await nextTick()

    const card = wrapper.get(`[data-toast-id="${id}"]`)
    expect(card.attributes('role')).toBe('alert')
    expect(card.attributes('aria-live')).toBe('assertive')
    expect(card.attributes('aria-atomic')).toBe('true')

    const detailsButton = card.get('button[aria-expanded="false"]')
    expect(detailsButton.attributes('aria-expanded')).toBe('false')
    expect(detailsButton.attributes('aria-controls')).toBeUndefined()

    await detailsButton.trigger('click')
    expect(detailsButton.attributes('aria-expanded')).toBe('true')
    expect(detailsButton.attributes('aria-controls')).toBe(`paper-toast-details-${id}`)
    const details = card.get('pre.paper-toast__details')
    expect(details.text()).toContain('status: 503')
    expect(details.attributes('aria-label')).toBe('Error details for Network error')
    expect(card.find('.paper-toast__countdown').exists()).toBe(false)

    await detailsButton.trigger('click')
    expect(detailsButton.attributes('aria-expanded')).toBe('false')
    expect(detailsButton.attributes('aria-controls')).toBeUndefined()
    expect(card.find('pre.paper-toast__details').exists()).toBe(false)

    vi.advanceTimersByTime(60_000)
    expect(store.toasts).toHaveLength(1)

    store.remove(id)
    await nextTick()
    expect(wrapper.find(`[data-toast-id="${id}"]`).exists()).toBe(false)
  })

  it('primes an empty polite region before announcing a newly added non-error toast', async () => {
    const store = useToastStore()

    wrapper = mount(PaperToastContainer)
    await nextTick()

    const announcer = wrapper.get('[data-toast-polite-announcer]')
    expect(announcer.attributes('role')).toBe('status')
    expect(announcer.text()).toBe('')

    const id = store.success('Capture saved to inbox', 0)
    await nextTick()
    await flushPromises()
    await nextTick()

    const card = wrapper.get(`[data-toast-id="${id}"]`)
    expect(card.text()).toContain('Capture saved to inbox')
    expect(card.attributes('role')).toBeUndefined()
    expect(card.attributes('aria-live')).toBeUndefined()
    expect(card.attributes('aria-atomic')).toBeUndefined()
    expect(announcer.text()).toBe('Capture saved to inbox')
  })

  it('lets a persistent actionless error receipt be dismissed', async () => {
    const store = useToastStore()
    const id = store.error('Persistent network error')

    wrapper = mount(PaperToastContainer)
    await nextTick()

    const card = wrapper.get(`[data-toast-id="${id}"]`)
    expect(card.find('.paper-toast__undo').exists()).toBe(false)

    const dismissButton = card.get('button[aria-label="Dismiss notification"]')
    await dismissButton.trigger('click')

    expect(store.toasts).toHaveLength(0)
  })

  it.each([
    ['en', 'Show details', 'Hide details', 'Copy details', 'Copied', 'Copy failed', 'Dismiss notification', 'Error details for Network error'],
    ['it', 'Mostra dettagli', 'Nascondi dettagli', 'Copia dettagli', 'Copiato', 'Copia non riuscita', 'Chiudi la notifica', 'Dettagli dell’errore: Network error'],
    ['es', 'Mostrar detalles', 'Ocultar detalles', 'Copiar detalles', 'Copiado', 'No se pudo copiar', 'Cerrar la notificación', 'Detalles del error: Network error'],
  ] as Array<[SupportedLocale, string, string, string, string, string, string, string]>)(
    'localizes persistent error receipt controls in %s',
    async (locale, show, hide, copy, copied, copyFailed, dismiss, errorDetails) => {
      i18n.global.locale.value = locale
      const writeText = vi.fn().mockResolvedValue(undefined)
      Object.defineProperty(navigator, 'clipboard', {
        configurable: true,
        value: { writeText },
      })
      const store = useToastStore()
      const id = store.error('Network error', undefined, { details: 'status: 503' })

      wrapper = mount(PaperToastContainer)
      await nextTick()

      const card = wrapper.get(`[data-toast-id="${id}"]`)
      const detailsButton = card.get('button[aria-expanded="false"]')
      const copyButton = card.findAll('button').find((button) => button.text() === copy)
      expect(detailsButton.text()).toBe(show)
      expect(copyButton).toBeDefined()
      expect(card.get(`button[aria-label="${dismiss}"]`).attributes('aria-label')).toBe(dismiss)

      await detailsButton.trigger('click')
      expect(detailsButton.text()).toBe(hide)
      expect(card.get('pre.paper-toast__details').attributes('aria-label')).toBe(errorDetails)

      await copyButton!.trigger('click')
      await flushPromises()
      expect(copyButton!.text()).toBe(copied)

      writeText.mockRejectedValueOnce(new Error('clipboard denied'))
      Object.defineProperty(document, 'execCommand', {
        configurable: true,
        value: vi.fn().mockReturnValue(false),
      })
      await copyButton!.trigger('click')
      await flushPromises()
      expect(copyButton!.text()).toBe(copyFailed)

      vi.advanceTimersByTime(60_000)
      expect(store.toasts).toHaveLength(1)
    },
  )
})

/**
 * Toast labels tell the truth about what happened (#1970).
 *
 * The stamp used to be derived from the Paper TONE, so every success read
 * "APPLIED" — on an inbox save, on a queued triage, and on an approval whose
 * own pane simultaneously said "not yet applied". These assertions read the
 * rendered stamp, never a screenshot or a delayed query: toasts here are
 * created with `duration: 0`, which disables the auto-dismiss timer entirely.
 */
describe('PaperToastContainer outcome labels', () => {
  let wrapper: VueWrapper | null = null

  beforeEach(() => {
    setActivePinia(createPinia())
  })

  afterEach(() => {
    wrapper?.unmount()
    wrapper = null
  })

  async function stampFor(type: Toast['type'], label?: ToastLabel): Promise<string> {
    const store = useToastStore()
    store.show('message body', type, 0, label ? { label } : {})
    wrapper = mount(PaperToastContainer)
    await nextTick()
    return wrapper.find('.paper-toast__head .tagstamp').text()
  }

  it.each([
    ['saved', 'Saved'],
    ['queued', 'Queued'],
    ['approved', 'Approved'],
    ['applied', 'Applied'],
  ] as Array<[ToastLabel, string]>)(
    'stamps a %s success toast "%s"',
    async (label, expected) => {
      expect(await stampFor('success', label)).toBe(expected)
    },
  )

  it('never stamps the approve toast with the applied label', async () => {
    // The AC's negative assertion: approving is step 1 of 2, so the toast must
    // not claim the board was written. Substring, not equality — "Applied"
    // must not appear anywhere in the stamp.
    const stamp = await stampFor('success', 'approved')
    expect(stamp).not.toContain('Applied')
  })

  it('falls back to a severity word, not an action word, for an unlabelled success', async () => {
    // This is the reported defect in its purest form: before the fix an
    // unlabelled success rendered the success TONE's name — "Applied".
    const stamp = await stampFor('success')
    expect(stamp).toBe('Done')
    expect(stamp).not.toContain('Applied')
  })

  it.each([
    ['error', 'Failed'],
    ['warning', 'Warning'],
    ['info', 'Noted'],
  ] as Array<[Toast['type'], string]>)(
    'stamps an unlabelled %s toast "%s"',
    async (type, expected) => {
      // The old fallbacks were tone names too — an error toast read "OVERDUE".
      expect(await stampFor(type, undefined)).toBe(expected)
    },
  )

  it('exposes the label kind as a data attribute independent of locale', async () => {
    const store = useToastStore()
    store.show('message body', 'success', 0, { label: 'saved' })
    wrapper = mount(PaperToastContainer)
    await nextTick()
    expect(wrapper.find('.paper-toast').attributes('data-label')).toBe('saved')
  })

  it('translates the stamp rather than hardcoding English', async () => {
    i18n.global.locale.value = 'it'
    try {
      expect(await stampFor('success', 'saved')).toBe('Salvato')
    } finally {
      i18n.global.locale.value = 'en'
    }
  })
})
