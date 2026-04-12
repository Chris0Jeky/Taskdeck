import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import TdInlineAlert from '../../../components/ui/TdInlineAlert.vue'

describe('TdInlineAlert', () => {
  it('renders slot content as alert message', () => {
    const wrapper = mount(TdInlineAlert, {
      slots: { default: 'Something went wrong.' },
    })
    expect(wrapper.text()).toContain('Something went wrong.')
  })

  it('applies info variant class by default', () => {
    const wrapper = mount(TdInlineAlert)
    expect(wrapper.classes()).toContain('td-inline-alert--info')
  })

  it.each(['info', 'success', 'warning', 'error'] as const)(
    'applies %s variant class',
    (variant) => {
      const wrapper = mount(TdInlineAlert, { props: { variant } })
      expect(wrapper.classes()).toContain(`td-inline-alert--${variant}`)
    },
  )

  it('has role="alert" for accessibility', () => {
    const wrapper = mount(TdInlineAlert)
    expect(wrapper.attributes('role')).toBe('alert')
  })

  it('does not show dismiss button by default', () => {
    const wrapper = mount(TdInlineAlert)
    expect(wrapper.find('.td-inline-alert__dismiss').exists()).toBe(false)
  })

  it('shows dismiss button when dismissible is true', () => {
    const wrapper = mount(TdInlineAlert, { props: { dismissible: true } })
    const dismissBtn = wrapper.find('.td-inline-alert__dismiss')
    expect(dismissBtn.exists()).toBe(true)
    expect(dismissBtn.attributes('aria-label')).toBe('Dismiss alert')
  })

  it('emits dismiss event when dismiss button is clicked', async () => {
    const wrapper = mount(TdInlineAlert, { props: { dismissible: true } })
    await wrapper.find('.td-inline-alert__dismiss').trigger('click')
    expect(wrapper.emitted('dismiss')).toHaveLength(1)
  })

  it('does not emit dismiss when not dismissible', () => {
    const wrapper = mount(TdInlineAlert)
    expect(wrapper.emitted('dismiss')).toBeUndefined()
  })
})
