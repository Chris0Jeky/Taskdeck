import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import TdBadge from '../../../components/ui/TdBadge.vue'
import TdTag from '../../../components/ui/TdTag.vue'
import TdEmptyState from '../../../components/ui/TdEmptyState.vue'

describe('TdBadge', () => {
  it('renders slot content', () => {
    const wrapper = mount(TdBadge, { slots: { default: 'New' } })
    expect(wrapper.text()).toBe('New')
  })

  it('defaults to default variant', () => {
    const wrapper = mount(TdBadge)
    expect(wrapper.classes()).toContain('td-badge--default')
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

  it('defaults to md size', () => {
    const wrapper = mount(TdBadge)
    expect(wrapper.classes()).toContain('td-badge--md')
  })
})

describe('TdTag', () => {
  it('renders slot content', () => {
    const wrapper = mount(TdTag, { slots: { default: 'Feature' } })
    expect(wrapper.text()).toContain('Feature')
  })

  it('shows remove button when removable', () => {
    const wrapper = mount(TdTag, { props: { removable: true } })
    expect(wrapper.find('.td-tag__remove').exists()).toBe(true)
  })

  it('hides remove button by default', () => {
    const wrapper = mount(TdTag)
    expect(wrapper.find('.td-tag__remove').exists()).toBe(false)
  })

  it('emits remove on button click', async () => {
    const wrapper = mount(TdTag, { props: { removable: true } })
    await wrapper.find('.td-tag__remove').trigger('click')
    expect(wrapper.emitted('remove')).toHaveLength(1)
  })

  it('applies custom color via CSS variable', () => {
    const wrapper = mount(TdTag, { props: { color: '#ff0000' } })
    expect(wrapper.classes()).toContain('td-tag--custom')
    const style = wrapper.attributes('style') ?? ''
    expect(style).toContain('--td-tag-color: #ff0000')
  })

  it('does not apply custom class when no color', () => {
    const wrapper = mount(TdTag)
    expect(wrapper.classes()).not.toContain('td-tag--custom')
  })
})

describe('TdEmptyState', () => {
  it('renders title', () => {
    const wrapper = mount(TdEmptyState, { props: { title: 'No items' } })
    expect(wrapper.find('.td-empty-state__title').text()).toBe('No items')
  })

  it('renders description when provided', () => {
    const wrapper = mount(TdEmptyState, {
      props: { title: 'No items', description: 'Start by adding one.' },
    })
    expect(wrapper.find('.td-empty-state__description').text()).toBe('Start by adding one.')
  })

  it('does not render description when empty', () => {
    const wrapper = mount(TdEmptyState, { props: { title: 'No items' } })
    expect(wrapper.find('.td-empty-state__description').exists()).toBe(false)
  })

  it('renders icon slot', () => {
    const wrapper = mount(TdEmptyState, {
      props: { title: 'No items' },
      slots: { icon: '<span class="test-icon">!</span>' },
    })
    expect(wrapper.find('.td-empty-state__icon .test-icon').exists()).toBe(true)
  })

  it('renders action slot', () => {
    const wrapper = mount(TdEmptyState, {
      props: { title: 'No items' },
      slots: { action: '<button class="test-action">Add</button>' },
    })
    expect(wrapper.find('.td-empty-state__action .test-action').exists()).toBe(true)
  })

  it('hides icon area when no icon slot', () => {
    const wrapper = mount(TdEmptyState, { props: { title: 'No items' } })
    expect(wrapper.find('.td-empty-state__icon').exists()).toBe(false)
  })

  it('hides action area when no action slot', () => {
    const wrapper = mount(TdEmptyState, { props: { title: 'No items' } })
    expect(wrapper.find('.td-empty-state__action').exists()).toBe(false)
  })
})
