import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import PaperKbd from '../../../components/paper/PaperKbd.vue'

describe('PaperKbd', () => {
  it('renders a <kbd> element by default', () => {
    const wrapper = mount(PaperKbd, { slots: { default: '⌘' } })
    expect(wrapper.element.tagName).toBe('KBD')
    expect(wrapper.text()).toBe('⌘')
  })

  it('applies the .pkbd class', () => {
    const wrapper = mount(PaperKbd, { slots: { default: 'K' } })
    expect(wrapper.classes()).toContain('pkbd')
    expect(wrapper.classes()).not.toContain('pkbd-light')
  })

  it('applies .pkbd-light when light=true', () => {
    const wrapper = mount(PaperKbd, { props: { light: true }, slots: { default: 'space' } })
    expect(wrapper.classes()).toContain('pkbd-light')
  })

  it('handles wide labels without breaking', () => {
    const wrapper = mount(PaperKbd, { slots: { default: '⌘K' } })
    expect(wrapper.text()).toBe('⌘K')
  })
})
