import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import TdSkeleton from '../../../components/ui/TdSkeleton.vue'

describe('TdSkeleton', () => {
  it('renders with default dimensions via CSS v-bind', () => {
    // SEC-29: width/height are now applied via v-bind() in scoped CSS
    // instead of inline :style binding. Vue sets CSS custom properties
    // as inline styles internally, so we verify the component mounts
    // and has the expected class.
    const wrapper = mount(TdSkeleton)
    expect(wrapper.classes()).toContain('td-skeleton')
    // The underlying style attribute will contain Vue-generated CSS
    // custom property names for v-bind, not direct width/height.
    const style = wrapper.attributes('style') ?? ''
    expect(style).not.toContain('width:')
    expect(style).not.toContain('height:')
  })

  it('accepts custom width and height props', () => {
    // SEC-29: props still flow through to CSS via v-bind(); we verify
    // the component accepts them without error.
    const wrapper = mount(TdSkeleton, { props: { width: '200px', height: '3rem' } })
    expect(wrapper.classes()).toContain('td-skeleton')
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
