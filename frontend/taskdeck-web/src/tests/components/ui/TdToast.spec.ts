import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import TdToast from '../../../components/ui/TdToast.vue'

describe('TdToast', () => {
  it('renders the message text', () => {
    const wrapper = mount(TdToast, { props: { message: 'Board created' } })
    expect(wrapper.find('.td-toast__message').text()).toBe('Board created')
  })

  it('applies info variant class by default', () => {
    const wrapper = mount(TdToast, { props: { message: 'Info message' } })
    expect(wrapper.classes()).toContain('td-toast--info')
  })

  it.each(['info', 'success', 'warning', 'error'] as const)(
    'applies %s variant class',
    (variant) => {
      const wrapper = mount(TdToast, { props: { message: 'msg', variant } })
      expect(wrapper.classes()).toContain(`td-toast--${variant}`)
    },
  )

  it('has role="status" and aria-live="polite" for accessibility', () => {
    const wrapper = mount(TdToast, { props: { message: 'test' } })
    expect(wrapper.attributes('role')).toBe('status')
    expect(wrapper.attributes('aria-live')).toBe('polite')
  })

  it('shows dismiss button by default (dismissible=true)', () => {
    const wrapper = mount(TdToast, { props: { message: 'test' } })
    const dismissBtn = wrapper.find('.td-toast__dismiss')
    expect(dismissBtn.exists()).toBe(true)
    expect(dismissBtn.attributes('aria-label')).toBe('Dismiss')
  })

  it('hides dismiss button when dismissible is false', () => {
    const wrapper = mount(TdToast, { props: { message: 'test', dismissible: false } })
    expect(wrapper.find('.td-toast__dismiss').exists()).toBe(false)
  })

  it('emits dismiss event when dismiss button is clicked', async () => {
    const wrapper = mount(TdToast, { props: { message: 'test' } })
    await wrapper.find('.td-toast__dismiss').trigger('click')
    expect(wrapper.emitted('dismiss')).toHaveLength(1)
  })
})
