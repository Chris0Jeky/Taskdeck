import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import PaperCaptureNib from '../../../../views/paper/inbox/PaperCaptureNib.vue'

describe('PaperCaptureNib', () => {
  beforeEach(() => {
    // jsdom focus only works when the element is attached to the document.
    document.body.innerHTML = ''
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('focuses the input on mount', async () => {
    const wrapper = mount(PaperCaptureNib, { attachTo: document.body })
    await nextTick()
    const textarea = wrapper.find('textarea').element as HTMLTextAreaElement
    expect(document.activeElement).toBe(textarea)
    wrapper.unmount()
  })

  it('emits submit on Enter with the trimmed text', async () => {
    const wrapper = mount(PaperCaptureNib)
    const textarea = wrapper.find('textarea')
    await textarea.setValue('  Look into local-first conflict resolution  ')
    await textarea.trigger('keydown', { key: 'Enter' })

    const events = wrapper.emitted('submit')
    expect(events).toBeDefined()
    expect(events?.[0]).toEqual(['Look into local-first conflict resolution'])
    // After submit the input is cleared.
    expect((textarea.element as HTMLTextAreaElement).value).toBe('')
  })

  it('does not emit on Enter when the input is empty / whitespace-only', async () => {
    const wrapper = mount(PaperCaptureNib)
    const textarea = wrapper.find('textarea')
    await textarea.setValue('   ')
    await textarea.trigger('keydown', { key: 'Enter' })
    expect(wrapper.emitted('submit')).toBeUndefined()
  })

  it('inserts a newline on Shift+Enter and does NOT submit', async () => {
    const wrapper = mount(PaperCaptureNib)
    const textarea = wrapper.find('textarea')
    await textarea.setValue('first line')
    // Shift+Enter — the component must not preventDefault, and must not emit.
    const event = new KeyboardEvent('keydown', { key: 'Enter', shiftKey: true, cancelable: true })
    textarea.element.dispatchEvent(event)
    expect(event.defaultPrevented).toBe(false)
    expect(wrapper.emitted('submit')).toBeUndefined()
  })

  it('renders the static ember placeholder while bleeding (TODO: ink bleed)', async () => {
    const wrapper = mount(PaperCaptureNib, { props: { bleeding: true } })
    expect(wrapper.find('[data-testid="paper-nib-bleed"]').exists()).toBe(true)
    expect(wrapper.find('textarea').exists()).toBe(false)
  })

  it('accepts a long capture without truncation (wraps at 80ch via CSS)', async () => {
    // The 80ch wrap is enforced by the component's <style scoped> rule —
    // jsdom doesn't apply scoped styles, so we instead verify the contract
    // at the data layer: the textarea round-trips arbitrarily long content
    // back through the submit event without truncation.
    const wrapper = mount(PaperCaptureNib)
    const textarea = wrapper.find('textarea')
    const long = 'a '.repeat(120).trim() // ~240 chars
    await textarea.setValue(long)
    await textarea.trigger('keydown', { key: 'Enter' })
    const events = wrapper.emitted('submit')
    expect(events).toBeDefined()
    expect((events?.[0]?.[0] as string).length).toBe(long.length)
  })
})
