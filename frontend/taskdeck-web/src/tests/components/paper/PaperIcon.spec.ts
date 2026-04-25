import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import PaperIcon from '../../../components/paper/PaperIcon.vue'
import { PAPER_ICON_SHAPES } from '../../../components/paper/paperIconPaths'

describe('PaperIcon', () => {
  it('renders a hairline svg with the default 14px class', () => {
    const wrapper = mount(PaperIcon, { props: { name: 'plus' } })
    const svg = wrapper.find('svg')
    expect(svg.exists()).toBe(true)
    expect(svg.classes()).toContain('hl-icon')
    expect(svg.classes()).not.toContain('hl-icon-md')
    expect(svg.classes()).not.toContain('hl-icon-lg')
  })

  it.each([
    [16, 'hl-icon-md'],
    [20, 'hl-icon-lg'],
  ] as const)('applies the %s size class for size=%i', (size, expected) => {
    const wrapper = mount(PaperIcon, { props: { name: 'plus', size } })
    expect(wrapper.find('svg').classes()).toContain(expected)
  })

  it('renders the path data for the requested icon', () => {
    const wrapper = mount(PaperIcon, { props: { name: 'plus' } })
    expect(wrapper.find('svg').attributes('data-icon')).toBe('plus')
    const path = wrapper.find('path')
    expect(path.exists()).toBe(true)
    expect(path.attributes('d')).toBe(PAPER_ICON_SHAPES.plus[0]!.kind === 'path' ? PAPER_ICON_SHAPES.plus[0].d : '')
  })

  it('renders compound icons (search uses circle + path)', () => {
    const wrapper = mount(PaperIcon, { props: { name: 'search' } })
    expect(wrapper.find('circle').exists()).toBe(true)
    expect(wrapper.find('path').exists()).toBe(true)
  })

  it('marks icon aria-hidden by default and exposes role/label when supplied', () => {
    const hidden = mount(PaperIcon, { props: { name: 'plus' } })
    expect(hidden.find('svg').attributes('aria-hidden')).toBe('true')

    const labelled = mount(PaperIcon, { props: { name: 'plus', label: 'Add' } })
    expect(labelled.find('svg').attributes('role')).toBe('img')
    expect(labelled.find('svg').attributes('aria-label')).toBe('Add')
    expect(labelled.find('svg').attributes('aria-hidden')).toBeUndefined()
  })

  it('every named icon resolves to at least one shape', () => {
    for (const name of Object.keys(PAPER_ICON_SHAPES) as (keyof typeof PAPER_ICON_SHAPES)[]) {
      const wrapper = mount(PaperIcon, { props: { name } })
      expect(wrapper.findAll('path, circle, rect').length).toBeGreaterThan(0)
    }
  })
})
