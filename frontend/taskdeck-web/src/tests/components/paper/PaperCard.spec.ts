import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import PaperCard from '../../../components/paper/PaperCard.vue'

describe('PaperCard', () => {
  it('renders default slot in a .card div', () => {
    const wrapper = mount(PaperCard, { slots: { default: '<p>hello</p>' } })
    expect(wrapper.element.tagName).toBe('DIV')
    expect(wrapper.classes()).toContain('card')
    expect(wrapper.find('p').text()).toBe('hello')
  })

  it.each([
    ['flat', 'card'],
    ['lift', 'card-lift'],
    ['well', 'well'],
  ] as const)('maps %s variant to %s class', (variant, expected) => {
    const wrapper = mount(PaperCard, { props: { variant }, slots: { default: 'x' } })
    expect(wrapper.classes()).toContain(expected)
  })

  it('adds .halo-ember when halo=true', () => {
    const wrapper = mount(PaperCard, { props: { halo: true }, slots: { default: 'x' } })
    expect(wrapper.classes()).toContain('halo-ember')
    expect(wrapper.attributes('data-halo')).toBe('true')
  })

  it('omits halo class by default', () => {
    const wrapper = mount(PaperCard, { slots: { default: 'x' } })
    expect(wrapper.classes()).not.toContain('halo-ember')
  })

  it('renders as the requested element', () => {
    const wrapper = mount(PaperCard, { props: { as: 'article' }, slots: { default: 'x' } })
    expect(wrapper.element.tagName).toBe('ARTICLE')
  })
})
