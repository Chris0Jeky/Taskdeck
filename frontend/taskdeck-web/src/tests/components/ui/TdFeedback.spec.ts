import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import TdToast from '../../../components/ui/TdToast.vue'
import TdInlineAlert from '../../../components/ui/TdInlineAlert.vue'
import TdSkeleton from '../../../components/ui/TdSkeleton.vue'
import TdSpinner from '../../../components/ui/TdSpinner.vue'

describe('TdToast', () => {
  it('renders message', () => {
    const wrapper = mount(TdToast, { props: { message: 'Saved!' } })
    expect(wrapper.text()).toContain('Saved!')
  })

  it('applies variant class', () => {
    const wrapper = mount(TdToast, { props: { message: 'OK', variant: 'success' } })
    expect(wrapper.classes()).toContain('td-toast--success')
  })

  it('defaults to info variant', () => {
    const wrapper = mount(TdToast, { props: { message: 'Info' } })
    expect(wrapper.classes()).toContain('td-toast--info')
  })

  it.each(['info', 'success', 'warning', 'error'] as const)(
    'renders %s variant',
    (variant) => {
      const wrapper = mount(TdToast, { props: { message: 'Test', variant } })
      expect(wrapper.classes()).toContain(`td-toast--${variant}`)
    },
  )

  it('shows dismiss button when dismissible', () => {
    const wrapper = mount(TdToast, { props: { message: 'Test', dismissible: true } })
    expect(wrapper.find('.td-toast__dismiss').exists()).toBe(true)
  })

  it('hides dismiss button when not dismissible', () => {
    const wrapper = mount(TdToast, { props: { message: 'Test', dismissible: false } })
    expect(wrapper.find('.td-toast__dismiss').exists()).toBe(false)
  })

  it('emits dismiss on button click', async () => {
    const wrapper = mount(TdToast, { props: { message: 'Test' } })
    await wrapper.find('.td-toast__dismiss').trigger('click')
    expect(wrapper.emitted('dismiss')).toHaveLength(1)
  })

  it('has status role for screen readers', () => {
    const wrapper = mount(TdToast, { props: { message: 'Test' } })
    expect(wrapper.attributes('role')).toBe('status')
  })
})

describe('TdInlineAlert', () => {
  it('renders slot content', () => {
    const wrapper = mount(TdInlineAlert, { slots: { default: 'Something went wrong' } })
    expect(wrapper.text()).toContain('Something went wrong')
  })

  it('applies variant class', () => {
    const wrapper = mount(TdInlineAlert, { props: { variant: 'error' } })
    expect(wrapper.classes()).toContain('td-inline-alert--error')
  })

  it('defaults to info variant', () => {
    const wrapper = mount(TdInlineAlert)
    expect(wrapper.classes()).toContain('td-inline-alert--info')
  })

  it('has alert role', () => {
    const wrapper = mount(TdInlineAlert)
    expect(wrapper.attributes('role')).toBe('alert')
  })

  it('shows dismiss button when dismissible', () => {
    const wrapper = mount(TdInlineAlert, { props: { dismissible: true } })
    expect(wrapper.find('.td-inline-alert__dismiss').exists()).toBe(true)
  })

  it('hides dismiss button by default', () => {
    const wrapper = mount(TdInlineAlert)
    expect(wrapper.find('.td-inline-alert__dismiss').exists()).toBe(false)
  })

  it('emits dismiss on button click', async () => {
    const wrapper = mount(TdInlineAlert, { props: { dismissible: true } })
    await wrapper.find('.td-inline-alert__dismiss').trigger('click')
    expect(wrapper.emitted('dismiss')).toHaveLength(1)
  })
})

describe('TdSkeleton', () => {
  it('renders a div', () => {
    const wrapper = mount(TdSkeleton)
    expect(wrapper.find('.td-skeleton').exists()).toBe(true)
  })

  it('is aria-hidden', () => {
    const wrapper = mount(TdSkeleton)
    expect(wrapper.attributes('aria-hidden')).toBe('true')
  })

  it('applies rounded class by default', () => {
    const wrapper = mount(TdSkeleton)
    expect(wrapper.classes()).toContain('td-skeleton--rounded')
  })

  it('applies circle class', () => {
    const wrapper = mount(TdSkeleton, { props: { circle: true } })
    expect(wrapper.classes()).toContain('td-skeleton--circle')
    expect(wrapper.classes()).not.toContain('td-skeleton--rounded')
  })

  it('applies custom width and height', () => {
    const wrapper = mount(TdSkeleton, { props: { width: '200px', height: '2rem' } })
    const style = wrapper.attributes('style') ?? ''
    expect(style).toContain('width: 200px')
    expect(style).toContain('height: 2rem')
  })
})

describe('TdSpinner', () => {
  it('renders with status role', () => {
    const wrapper = mount(TdSpinner)
    expect(wrapper.attributes('role')).toBe('status')
  })

  it('renders default label', () => {
    const wrapper = mount(TdSpinner)
    expect(wrapper.find('.td-spinner__label').text()).toBe('Loading')
  })

  it('renders custom label', () => {
    const wrapper = mount(TdSpinner, { props: { label: 'Processing...' } })
    expect(wrapper.find('.td-spinner__label').text()).toBe('Processing...')
  })

  it.each(['sm', 'md', 'lg'] as const)('applies %s size class', (size) => {
    const wrapper = mount(TdSpinner, { props: { size } })
    expect(wrapper.classes()).toContain(`td-spinner--${size}`)
  })

  it('renders svg element', () => {
    const wrapper = mount(TdSpinner)
    expect(wrapper.find('.td-spinner__svg').exists()).toBe(true)
  })
})
