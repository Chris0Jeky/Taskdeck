import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import TdSkeleton from '../../../components/ui/TdSkeleton.vue'

describe('TdSkeleton', () => {
  it('renders with default dimensions', () => {
    const wrapper = mount(TdSkeleton)
    const style = wrapper.attributes('style')
    expect(style).toContain('width: 100%')
    expect(style).toContain('height: 1rem')
  })

  it('applies custom width and height', () => {
    const wrapper = mount(TdSkeleton, { props: { width: '200px', height: '3rem' } })
    const style = wrapper.attributes('style')
    expect(style).toContain('width: 200px')
    expect(style).toContain('height: 3rem')
  })

  it('applies rounded class by default', () => {
    const wrapper = mount(TdSkeleton)
    expect(wrapper.classes()).toContain('td-skeleton--rounded')
    expect(wrapper.classes()).not.toContain('td-skeleton--circle')
  })

  it('applies circle class when circle prop is true', () => {
    const wrapper = mount(TdSkeleton, { props: { circle: true } })
    expect(wrapper.classes()).toContain('td-skeleton--circle')
    expect(wrapper.classes()).not.toContain('td-skeleton--rounded')
  })

  it('does not apply rounded class when circle is true, even with rounded default', () => {
    const wrapper = mount(TdSkeleton, { props: { circle: true, rounded: true } })
    expect(wrapper.classes()).toContain('td-skeleton--circle')
    expect(wrapper.classes()).not.toContain('td-skeleton--rounded')
  })

  it('does not apply rounded class when rounded is false', () => {
    const wrapper = mount(TdSkeleton, { props: { rounded: false } })
    expect(wrapper.classes()).not.toContain('td-skeleton--rounded')
    expect(wrapper.classes()).not.toContain('td-skeleton--circle')
  })

  it('sets aria-hidden="true" to hide from screen readers', () => {
    const wrapper = mount(TdSkeleton)
    expect(wrapper.attributes('aria-hidden')).toBe('true')
  })
})
