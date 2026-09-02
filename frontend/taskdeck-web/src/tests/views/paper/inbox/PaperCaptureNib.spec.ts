import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import { createI18n } from 'vue-i18n'
import PaperCaptureNib from '../../../../views/paper/inbox/PaperCaptureNib.vue'
import en from '../../../../locales/en'

type NibProps = {
  bleeding?: boolean
  submitting?: boolean
  invalid?: boolean
  errorId?: string | null
  activeBoardId?: string | null
  activeBoardName?: string | null
}

function mountNib(options: { attachTo?: HTMLElement; props?: NibProps } = {}) {
  const i18n = createI18n({
    legacy: false,
    locale: 'en',
    messages: { en },
  })
  return mount(PaperCaptureNib, {
    ...options,
    global: {
      plugins: [i18n],
    },
  })
}

describe('PaperCaptureNib', () => {
  beforeEach(() => {
    // jsdom focus only works when the element is attached to the document.
    document.body.innerHTML = ''
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('focuses the input on mount', async () => {
    const wrapper = mountNib({ attachTo: document.body })
    await nextTick()
    const textarea = wrapper.find('textarea').element as HTMLTextAreaElement
    expect(document.activeElement).toBe(textarea)
    wrapper.unmount()
  })

  it('emits submit on Enter with the trimmed text', async () => {
    const wrapper = mountNib()
    const textarea = wrapper.find('textarea')
    await textarea.setValue('  Look into local-first conflict resolution  ')
    await textarea.trigger('keydown', { key: 'Enter' })

    const events = wrapper.emitted('submit')
    expect(events).toBeDefined()
    expect(events?.[0]).toEqual(['Look into local-first conflict resolution'])
    // The parent clears only after async creation succeeds.
    expect((textarea.element as HTMLTextAreaElement).value).toBe('  Look into local-first conflict resolution  ')
  })

  it('does not submit while creation is already in flight', async () => {
    const wrapper = mountNib({ props: { submitting: true } })
    const textarea = wrapper.find('textarea')
    await textarea.setValue('Do not duplicate this capture')
    await textarea.trigger('keydown', { key: 'Enter' })

    expect(wrapper.emitted('submit')).toBeUndefined()
    expect(textarea.attributes('disabled')).toBeDefined()
  })

  it('exposes resetDraft so the parent can clear after success', async () => {
    const wrapper = mountNib()
    const textarea = wrapper.find('textarea')
    await textarea.setValue('Clear only after success')

    ;(wrapper.vm as unknown as { resetDraft: () => void }).resetDraft()
    await wrapper.vm.$nextTick()

    expect((textarea.element as HTMLTextAreaElement).value).toBe('')
  })

  it('does not emit on Enter when the input is empty / whitespace-only', async () => {
    const wrapper = mountNib()
    const textarea = wrapper.find('textarea')
    await textarea.setValue('   ')
    await textarea.trigger('keydown', { key: 'Enter' })
    expect(wrapper.emitted('submit')).toBeUndefined()
  })

  it('inserts a newline on Shift+Enter and does NOT submit', async () => {
    const wrapper = mountNib()
    const textarea = wrapper.find('textarea')
    await textarea.setValue('first line')
    // Shift+Enter — the component must not preventDefault, and must not emit.
    const event = new KeyboardEvent('keydown', { key: 'Enter', shiftKey: true, cancelable: true })
    textarea.element.dispatchEvent(event)
    expect(event.defaultPrevented).toBe(false)
    expect(wrapper.emitted('submit')).toBeUndefined()
  })

  it('does not submit while IME composition is active', async () => {
    const wrapper = mountNib()
    const textarea = wrapper.find('textarea')
    await textarea.setValue('input in progress')
    const event = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true, isComposing: true })
    textarea.element.dispatchEvent(event)

    expect(event.defaultPrevented).toBe(false)
    expect(wrapper.emitted('submit')).toBeUndefined()
  })

  it('renders the static ember placeholder while bleeding (TODO: ink bleed)', async () => {
    const wrapper = mountNib({ props: { bleeding: true } })
    expect(wrapper.find('[data-testid="paper-nib-bleed"]').exists()).toBe(true)
    expect(wrapper.find('textarea').exists()).toBe(false)
  })

  it('accepts a long capture without truncation (wraps at 80ch via CSS)', async () => {
    // The 80ch wrap is enforced by the component's <style scoped> rule —
    // jsdom doesn't apply scoped styles, so we instead verify the contract
    // at the data layer: the textarea round-trips arbitrarily long content
    // back through the submit event without truncation.
    const wrapper = mountNib()
    const textarea = wrapper.find('textarea')
    const long = 'a '.repeat(120).trim() // ~240 chars
    await textarea.setValue(long)
    await textarea.trigger('keydown', { key: 'Enter' })
    const events = wrapper.emitted('submit')
    expect(events).toBeDefined()
    expect((events?.[0]?.[0] as string).length).toBe(long.length)
  })

  it('submits the trimmed text from the visible Capture button', async () => {
    const wrapper = mountNib()
    const textarea = wrapper.find('textarea')
    const button = wrapper.get('button')

    expect(button.attributes('disabled')).toBeDefined()
    await textarea.setValue('  Capture from the button  ')
    expect(button.attributes('disabled')).toBeUndefined()
    await button.trigger('click')

    expect(wrapper.emitted('submit')?.[0]).toEqual(['Capture from the button'])
  })

  it('keeps the Capture button disabled for an empty or submitting nib', async () => {
    const wrapper = mountNib()
    const textarea = wrapper.find('textarea')
    const button = wrapper.get('button')

    await textarea.setValue('   ')
    expect(button.attributes('disabled')).toBeDefined()
    await wrapper.setProps({ submitting: true })
    await textarea.setValue('A capture already in flight')
    expect(button.attributes('disabled')).toBeDefined()
    await button.trigger('click')
    expect(wrapper.emitted('submit')).toBeUndefined()
  })

  it('states that board-less captures land in Inbox without a board', () => {
    const wrapper = mountNib()

    expect(wrapper.get('[data-testid="paper-nib-destination"]').text())
      .toBe('This capture lands in Inbox without a board, for triage.')
  })

  it('states that a board-scoped capture lands in Inbox linked to that board', () => {
    const wrapper = mountNib({
      props: { activeBoardId: 'board-active', activeBoardName: 'Active board' },
    })

    expect(wrapper.get('[data-testid="paper-nib-destination"]').text())
      .toBe('This capture lands in Inbox, linked to Active board, for triage.')
  })

  it('uses a safe board label when a scoped route has no board name', () => {
    const wrapper = mountNib({ props: { activeBoardId: 'board-active' } })

    expect(wrapper.get('[data-testid="paper-nib-destination"]').text())
      .toBe('This capture lands in Inbox, linked to the selected board, for triage.')
  })

  it('renders the variant shortcut for Win32 without a Mac modifier glyph', () => {
    vi.stubGlobal('navigator', {
      userAgentData: { platform: 'Win32' },
      userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)',
    })

    const wrapper = mountNib()
    const eyebrow = wrapper.get('.paper-nib__eyebrow').text()

    expect(eyebrow).toContain('Ctrl+;')
    expect(eyebrow).not.toContain('\u2318')
  })
})
