import { describe, expect, it, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import TdTooltip from '../../../components/ui/TdTooltip.vue'

describe('TdTooltip', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  it('renders trigger content', () => {
    const wrapper = mount(TdTooltip, {
      props: { text: 'Help text' },
      slots: { default: '<button>Hover me</button>' },
    })
    expect(wrapper.find('button').text()).toBe('Hover me')
  })

  it('does not show tooltip initially', () => {
    const wrapper = mount(TdTooltip, { props: { text: 'Help' } })
    expect(wrapper.find('.td-tooltip').exists()).toBe(false)
  })

  it('shows tooltip on mouseenter after delay', async () => {
    const wrapper = mount(TdTooltip, { props: { text: 'Help', delay: 100 } })
    await wrapper.trigger('mouseenter')
    vi.advanceTimersByTime(100)
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.td-tooltip').exists()).toBe(true)
    expect(wrapper.find('.td-tooltip').text()).toBe('Help')
  })

  it('hides tooltip on mouseleave', async () => {
    const wrapper = mount(TdTooltip, { props: { text: 'Help', delay: 0 } })
    await wrapper.trigger('mouseenter')
    vi.advanceTimersByTime(0)
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.td-tooltip').exists()).toBe(true)

    await wrapper.trigger('mouseleave')
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.td-tooltip').exists()).toBe(false)
  })

  it('shows tooltip on focusin', async () => {
    const wrapper = mount(TdTooltip, { props: { text: 'Help' } })
    await wrapper.trigger('focusin')
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.td-tooltip').exists()).toBe(true)
  })

  it('hides tooltip on focusout', async () => {
    const wrapper = mount(TdTooltip, { props: { text: 'Help' } })
    await wrapper.trigger('focusin')
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.td-tooltip').exists()).toBe(true)

    await wrapper.trigger('focusout')
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.td-tooltip').exists()).toBe(false)
  })

  it('has role="tooltip"', async () => {
    const wrapper = mount(TdTooltip, { props: { text: 'Help' } })
    await wrapper.trigger('focusin')
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.td-tooltip').attributes('role')).toBe('tooltip')
  })

  it.each(['top', 'bottom', 'left', 'right'] as const)(
    'applies %s position class',
    async (position) => {
      const wrapper = mount(TdTooltip, { props: { text: 'Help', position } })
      await wrapper.trigger('focusin')
      await wrapper.vm.$nextTick()
      expect(wrapper.find('.td-tooltip').classes()).toContain(`td-tooltip--${position}`)
    },
  )

  it('cancels show when mouseleave happens before delay', async () => {
    const wrapper = mount(TdTooltip, { props: { text: 'Help', delay: 500 } })
    await wrapper.trigger('mouseenter')
    vi.advanceTimersByTime(100)
    await wrapper.trigger('mouseleave')
    vi.advanceTimersByTime(500)
    await wrapper.vm.$nextTick()
    expect(wrapper.find('.td-tooltip').exists()).toBe(false)
  })
})
