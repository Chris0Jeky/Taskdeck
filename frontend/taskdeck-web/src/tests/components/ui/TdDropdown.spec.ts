import { describe, expect, it, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import TdDropdown from '../../../components/ui/TdDropdown.vue'

const escapeHandlers: Array<() => void> = []

vi.mock('../../../composables/useEscapeStack', () => ({
  registerEscapeHandler: vi.fn((handler: () => void) => {
    escapeHandlers.push(handler)
    return () => {
      const index = escapeHandlers.indexOf(handler)
      if (index >= 0) {
        escapeHandlers.splice(index, 1)
      }
    }
  }),
}))

describe('TdDropdown', () => {
  beforeEach(() => {
    escapeHandlers.splice(0, escapeHandlers.length)
  })

  it('renders trigger slot', () => {
    const wrapper = mount(TdDropdown, {
      props: { open: false },
      slots: { trigger: '<button class="test-trigger">Open</button>' },
    })
    expect(wrapper.find('.test-trigger').exists()).toBe(true)
  })

  it('hides panel when not open', () => {
    const wrapper = mount(TdDropdown, { props: { open: false } })
    expect(wrapper.find('.td-dropdown__panel').exists()).toBe(false)
  })

  it('shows panel when open', () => {
    const wrapper = mount(TdDropdown, {
      props: { open: true },
      slots: { default: '<div class="test-item">Item</div>' },
    })
    expect(wrapper.find('.td-dropdown__panel').exists()).toBe(true)
    expect(wrapper.find('.test-item').exists()).toBe(true)
  })

  it('panel has role="menu"', () => {
    const wrapper = mount(TdDropdown, { props: { open: true } })
    expect(wrapper.find('.td-dropdown__panel').attributes('role')).toBe('menu')
  })

  it('applies left alignment by default', () => {
    const wrapper = mount(TdDropdown, { props: { open: true } })
    expect(wrapper.find('.td-dropdown__panel').classes()).toContain('td-dropdown__panel--left')
  })

  it('applies right alignment when specified', () => {
    const wrapper = mount(TdDropdown, { props: { open: true, align: 'right' } })
    expect(wrapper.find('.td-dropdown__panel').classes()).toContain('td-dropdown__panel--right')
  })

  it('registers escape handler when opened', async () => {
    const wrapper = mount(TdDropdown, { props: { open: false } })
    expect(escapeHandlers.length).toBe(0)
    await wrapper.setProps({ open: true })
    expect(escapeHandlers.length).toBe(1)
  })

  it('emits close via escape handler', async () => {
    const wrapper = mount(TdDropdown, { props: { open: true } })
    expect(escapeHandlers.length).toBe(1)
    escapeHandlers[0]!()
    expect(wrapper.emitted('close')).toHaveLength(1)
  })

  it('unregisters escape handler when closed', async () => {
    const wrapper = mount(TdDropdown, { props: { open: true } })
    expect(escapeHandlers.length).toBe(1)
    await wrapper.setProps({ open: false })
    expect(escapeHandlers.length).toBe(0)
  })
})
