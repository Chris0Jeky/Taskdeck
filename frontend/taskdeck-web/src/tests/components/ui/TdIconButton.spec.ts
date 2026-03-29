import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import TdIconButton from '../../../components/ui/TdIconButton.vue'

describe('TdIconButton', () => {
  it('renders with aria-label', () => {
    const wrapper = mount(TdIconButton, { props: { label: 'Close' } })
    expect(wrapper.attributes('aria-label')).toBe('Close')
  })

  it('applies ghost variant by default', () => {
    const wrapper = mount(TdIconButton, { props: { label: 'Action' } })
    expect(wrapper.classes()).toContain('td-icon-btn--ghost')
  })

  it.each(['primary', 'secondary', 'ghost', 'danger'] as const)(
    'applies %s variant class',
    (variant) => {
      const wrapper = mount(TdIconButton, { props: { label: 'Action', variant } })
      expect(wrapper.classes()).toContain(`td-icon-btn--${variant}`)
    },
  )

  it.each(['sm', 'md', 'lg'] as const)('applies %s size class', (size) => {
    const wrapper = mount(TdIconButton, { props: { label: 'Action', size } })
    expect(wrapper.classes()).toContain(`td-icon-btn--${size}`)
  })

  it('emits click when clicked', async () => {
    const wrapper = mount(TdIconButton, { props: { label: 'Action' } })
    await wrapper.trigger('click')
    expect(wrapper.emitted('click')).toHaveLength(1)
  })

  it('does not emit click when disabled', async () => {
    const wrapper = mount(TdIconButton, { props: { label: 'Action', disabled: true } })
    await wrapper.trigger('click')
    expect(wrapper.emitted('click')).toBeUndefined()
  })

  it('does not emit click when loading', async () => {
    const wrapper = mount(TdIconButton, { props: { label: 'Action', loading: true } })
    await wrapper.trigger('click')
    expect(wrapper.emitted('click')).toBeUndefined()
  })

  it('shows spinner when loading', () => {
    const wrapper = mount(TdIconButton, { props: { label: 'Action', loading: true } })
    expect(wrapper.find('.td-icon-btn__spinner').exists()).toBe(true)
  })

  it('shows icon slot when not loading', () => {
    const wrapper = mount(TdIconButton, {
      props: { label: 'Action' },
      slots: { default: '<span class="test-icon">X</span>' },
    })
    expect(wrapper.find('.td-icon-btn__icon').exists()).toBe(true)
    expect(wrapper.find('.test-icon').exists()).toBe(true)
  })

  it('is always type button', () => {
    const wrapper = mount(TdIconButton, { props: { label: 'Action' } })
    expect(wrapper.attributes('type')).toBe('button')
  })
})
