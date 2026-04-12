import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import TdEmptyState from '../../../components/ui/TdEmptyState.vue'

describe('TdEmptyState', () => {
  it('renders the title', () => {
    const wrapper = mount(TdEmptyState, { props: { title: 'No boards yet' } })
    expect(wrapper.text()).toContain('No boards yet')
    expect(wrapper.find('.td-empty-state__title').text()).toBe('No boards yet')
  })

  it('renders description when provided', () => {
    const wrapper = mount(TdEmptyState, {
      props: { title: 'Nothing here', description: 'Create your first board to get started.' },
    })
    expect(wrapper.find('.td-empty-state__description').text()).toBe(
      'Create your first board to get started.',
    )
  })

  it('does not render description element when description is empty', () => {
    const wrapper = mount(TdEmptyState, { props: { title: 'Empty' } })
    expect(wrapper.find('.td-empty-state__description').exists()).toBe(false)
  })

  it('renders icon slot when provided', () => {
    const wrapper = mount(TdEmptyState, {
      props: { title: 'Empty' },
      slots: { icon: '<span data-testid="custom-icon">ICON</span>' },
    })
    expect(wrapper.find('.td-empty-state__icon').exists()).toBe(true)
    expect(wrapper.find('[data-testid="custom-icon"]').text()).toBe('ICON')
  })

  it('does not render icon wrapper when no icon slot is provided', () => {
    const wrapper = mount(TdEmptyState, { props: { title: 'Empty' } })
    expect(wrapper.find('.td-empty-state__icon').exists()).toBe(false)
  })

  it('renders action slot when provided', () => {
    const wrapper = mount(TdEmptyState, {
      props: { title: 'No data' },
      slots: { action: '<button>Create Board</button>' },
    })
    expect(wrapper.find('.td-empty-state__action').exists()).toBe(true)
    expect(wrapper.find('.td-empty-state__action button').text()).toBe('Create Board')
  })

  it('does not render action wrapper when no action slot is provided', () => {
    const wrapper = mount(TdEmptyState, { props: { title: 'Empty' } })
    expect(wrapper.find('.td-empty-state__action').exists()).toBe(false)
  })
})
