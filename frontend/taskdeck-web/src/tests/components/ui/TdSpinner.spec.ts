import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import TdSpinner from '../../../components/ui/TdSpinner.vue'

describe('TdSpinner', () => {
  it('renders with default label "Loading"', () => {
    const wrapper = mount(TdSpinner)
    expect(wrapper.find('.td-spinner__label').text()).toBe('Loading')
  })

  it('renders with custom label', () => {
    const wrapper = mount(TdSpinner, { props: { label: 'Saving...' } })
    expect(wrapper.find('.td-spinner__label').text()).toBe('Saving...')
  })

  it('applies md size class by default', () => {
    const wrapper = mount(TdSpinner)
    expect(wrapper.classes()).toContain('td-spinner--md')
  })

  it.each(['sm', 'md', 'lg'] as const)('applies %s size class', (size) => {
    const wrapper = mount(TdSpinner, { props: { size } })
    expect(wrapper.classes()).toContain(`td-spinner--${size}`)
  })

  it('has role="status" for accessibility', () => {
    const wrapper = mount(TdSpinner)
    expect(wrapper.attributes('role')).toBe('status')
  })

  it('renders an SVG spinner element', () => {
    const wrapper = mount(TdSpinner)
    const svg = wrapper.find('.td-spinner__svg')
    expect(svg.exists()).toBe(true)
    expect(svg.attributes('aria-hidden')).toBe('true')
  })

  it('contains track and arc path elements', () => {
    const wrapper = mount(TdSpinner)
    expect(wrapper.find('.td-spinner__track').exists()).toBe(true)
    expect(wrapper.find('.td-spinner__arc').exists()).toBe(true)
  })
})
