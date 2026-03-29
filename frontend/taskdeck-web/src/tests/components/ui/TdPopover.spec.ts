import { describe, expect, it, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import TdPopover from '../../../components/ui/TdPopover.vue'

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

describe('TdPopover', () => {
  beforeEach(() => {
    escapeHandlers.splice(0, escapeHandlers.length)
    document.body.innerHTML = ''
  })

  it('renders trigger slot', () => {
    const wrapper = mount(TdPopover, {
      props: { open: false },
      slots: { trigger: '<button class="test-trigger">Open</button>' },
    })
    expect(wrapper.find('.test-trigger').exists()).toBe(true)
  })

  it('hides panel when not open', () => {
    const wrapper = mount(TdPopover, { props: { open: false } })
    expect(wrapper.find('.td-popover__panel').exists()).toBe(false)
  })

  it('shows panel when open', () => {
    const wrapper = mount(TdPopover, {
      props: { open: true },
      slots: { default: '<div class="test-content">Content</div>' },
    })
    expect(wrapper.find('.td-popover__panel').exists()).toBe(true)
    expect(wrapper.find('.test-content').exists()).toBe(true)
  })

  it('applies left alignment by default', () => {
    const wrapper = mount(TdPopover, { props: { open: true } })
    expect(wrapper.find('.td-popover__panel').classes()).toContain('td-popover__panel--left')
  })

  it.each(['left', 'right', 'center'] as const)(
    'applies %s alignment',
    (align) => {
      const wrapper = mount(TdPopover, { props: { open: true, align } })
      expect(wrapper.find('.td-popover__panel').classes()).toContain(`td-popover__panel--${align}`)
    },
  )

  it.each(['top', 'bottom'] as const)(
    'applies %s position',
    (position) => {
      const wrapper = mount(TdPopover, { props: { open: true, position } })
      expect(wrapper.find('.td-popover__panel').classes()).toContain(`td-popover__panel--${position}`)
    },
  )

  it('registers escape handler when opened', async () => {
    const wrapper = mount(TdPopover, { props: { open: false } })
    expect(escapeHandlers.length).toBe(0)
    await wrapper.setProps({ open: true })
    expect(escapeHandlers.length).toBe(1)
  })

  it('emits close via escape handler', async () => {
    const wrapper = mount(TdPopover, { props: { open: true } })
    expect(escapeHandlers.length).toBe(1)
    escapeHandlers[0]!()
    expect(wrapper.emitted('close')).toHaveLength(1)
  })

  it('unregisters escape handler when closed', async () => {
    const wrapper = mount(TdPopover, { props: { open: true } })
    expect(escapeHandlers.length).toBe(1)
    await wrapper.setProps({ open: false })
    expect(escapeHandlers.length).toBe(0)
  })

  it('restores focus to the previously active element when unmounted while open', async () => {
    const trigger = document.createElement('button')
    document.body.appendChild(trigger)
    trigger.focus()

    const wrapper = mount(TdPopover, {
      props: { open: true },
      slots: {
        trigger: '<button type="button">Open</button>',
        default: '<button type="button">Content</button>',
      },
      attachTo: document.body,
    })

    await nextTick()
    expect(document.activeElement).not.toBe(trigger)

    wrapper.unmount()

    expect(document.activeElement).toBe(trigger)
  })
})
