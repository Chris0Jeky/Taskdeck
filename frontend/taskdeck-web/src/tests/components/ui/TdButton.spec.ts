import { describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import TdButton from '../../../components/ui/TdButton.vue'

describe('TdButton', () => {
  it('renders slot content', () => {
    const wrapper = mount(TdButton, { slots: { default: 'Click me' } })
    expect(wrapper.text()).toContain('Click me')
  })

  it('applies primary variant class by default', () => {
    const wrapper = mount(TdButton)
    expect(wrapper.classes()).toContain('td-btn--primary')
  })

  it.each(['primary', 'secondary', 'ghost', 'danger'] as const)(
    'applies %s variant class',
    (variant) => {
      const wrapper = mount(TdButton, { props: { variant } })
      expect(wrapper.classes()).toContain(`td-btn--${variant}`)
    },
  )

  it.each(['sm', 'md', 'lg'] as const)('applies %s size class', (size) => {
    const wrapper = mount(TdButton, { props: { size } })
    expect(wrapper.classes()).toContain(`td-btn--${size}`)
  })

  it('emits click when clicked', async () => {
    const wrapper = mount(TdButton)
    await wrapper.trigger('click')
    expect(wrapper.emitted('click')).toHaveLength(1)
  })

  it('does not emit click when disabled', async () => {
    const wrapper = mount(TdButton, { props: { disabled: true } })
    await wrapper.trigger('click')
    expect(wrapper.emitted('click')).toBeUndefined()
  })

  it('does not emit click when loading', async () => {
    const wrapper = mount(TdButton, { props: { loading: true } })
    await wrapper.trigger('click')
    expect(wrapper.emitted('click')).toBeUndefined()
  })

  it('sets disabled attribute when disabled', () => {
    const wrapper = mount(TdButton, { props: { disabled: true } })
    expect(wrapper.attributes('disabled')).toBeDefined()
  })

  it('sets disabled attribute when loading', () => {
    const wrapper = mount(TdButton, { props: { loading: true } })
    expect(wrapper.attributes('disabled')).toBeDefined()
  })

  it('shows spinner when loading', () => {
    const wrapper = mount(TdButton, { props: { loading: true } })
    expect(wrapper.find('.td-btn__spinner').exists()).toBe(true)
    expect(wrapper.classes()).toContain('td-btn--loading')
  })

  it('hides content visually when loading', () => {
    const wrapper = mount(TdButton, {
      props: { loading: true },
      slots: { default: 'Save' },
    })
    const content = wrapper.find('.td-btn__content--hidden')
    expect(content.exists()).toBe(true)
  })

  it('sets aria-busy when loading', () => {
    const wrapper = mount(TdButton, { props: { loading: true } })
    expect(wrapper.attributes('aria-busy')).toBe('true')
  })

  it('sets the type attribute', () => {
    const wrapper = mount(TdButton, { props: { type: 'submit' } })
    expect(wrapper.attributes('type')).toBe('submit')
  })

  it('defaults to button type', () => {
    const wrapper = mount(TdButton)
    expect(wrapper.attributes('type')).toBe('button')
  })
})
