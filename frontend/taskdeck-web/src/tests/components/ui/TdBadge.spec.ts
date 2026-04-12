import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import TdBadge from '../../../components/ui/TdBadge.vue'

describe('TdBadge', () => {
  it('renders slot content', () => {
    const wrapper = mount(TdBadge, { slots: { default: '3 items' } })
    expect(wrapper.text()).toBe('3 items')
  })

  it('applies default variant and md size classes by default', () => {
    const wrapper = mount(TdBadge)
    expect(wrapper.classes()).toContain('td-badge--default')
    expect(wrapper.classes()).toContain('td-badge--md')
  })

  it.each(['default', 'primary', 'success', 'warning', 'error', 'info'] as const)(
    'applies %s variant class',
    (variant) => {
      const wrapper = mount(TdBadge, { props: { variant } })
      expect(wrapper.classes()).toContain(`td-badge--${variant}`)
    },
  )

  it.each(['sm', 'md'] as const)('applies %s size class', (size) => {
    const wrapper = mount(TdBadge, { props: { size } })
    expect(wrapper.classes()).toContain(`td-badge--${size}`)
  })

  it('renders as a span element', () => {
    const wrapper = mount(TdBadge)
    expect(wrapper.element.tagName).toBe('SPAN')
  })
})
