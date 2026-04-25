import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import PaperTagstamp from '../../../components/paper/PaperTagstamp.vue'

describe('PaperTagstamp', () => {
  it('renders default slot inside a .tagstamp span', () => {
    const wrapper = mount(PaperTagstamp, { slots: { default: 'PROPOSED' } })
    expect(wrapper.element.tagName).toBe('SPAN')
    expect(wrapper.classes()).toContain('tagstamp')
    expect(wrapper.text()).toBe('PROPOSED')
  })

  it.each([
    ['ember', 'var(--ember)'],
    ['applied', 'var(--applied)'],
    ['overdue', 'var(--overdue)'],
    ['mute', 'var(--mute)'],
  ] as const)('applies %s tone color', (tone, expectedColor) => {
    const wrapper = mount(PaperTagstamp, {
      props: { tone },
      slots: { default: 'TAG' },
    })
    expect(wrapper.attributes('data-tone')).toBe(tone)
    const style = wrapper.attributes('style') ?? ''
    expect(style).toContain(expectedColor)
  })

  it('defaults to mute tone', () => {
    const wrapper = mount(PaperTagstamp, { slots: { default: 'X' } })
    expect(wrapper.attributes('data-tone')).toBe('mute')
  })
})
