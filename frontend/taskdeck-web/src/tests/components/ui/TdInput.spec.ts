import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import TdInput from '../../../components/ui/TdInput.vue'

describe('TdInput', () => {
  it('renders an input element', () => {
    const wrapper = mount(TdInput)
    expect(wrapper.find('input').exists()).toBe(true)
  })

  it('binds modelValue', () => {
    const wrapper = mount(TdInput, { props: { modelValue: 'hello' } })
    expect(wrapper.find('input').element.value).toBe('hello')
  })

  it('emits update:modelValue on input', async () => {
    const wrapper = mount(TdInput)
    await wrapper.find('input').setValue('test')
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual(['test'])
  })

  it('sets placeholder', () => {
    const wrapper = mount(TdInput, { props: { placeholder: 'Enter...' } })
    expect(wrapper.find('input').attributes('placeholder')).toBe('Enter...')
  })

  it('applies error class when error is true', () => {
    const wrapper = mount(TdInput, { props: { error: true } })
    expect(wrapper.find('input').classes()).toContain('td-input--error')
  })

  it('sets aria-invalid when error is true', () => {
    const wrapper = mount(TdInput, { props: { error: true } })
    expect(wrapper.find('input').attributes('aria-invalid')).toBe('true')
  })

  it('does not set aria-invalid when no error', () => {
    const wrapper = mount(TdInput)
    expect(wrapper.find('input').attributes('aria-invalid')).toBeUndefined()
  })

  it('sets disabled attribute', () => {
    const wrapper = mount(TdInput, { props: { disabled: true } })
    expect(wrapper.find('input').attributes('disabled')).toBeDefined()
  })

  it('sets readonly attribute', () => {
    const wrapper = mount(TdInput, { props: { readonly: true } })
    expect(wrapper.find('input').attributes('readonly')).toBeDefined()
  })

  it('passes id to input', () => {
    const wrapper = mount(TdInput, { props: { id: 'my-input' } })
    expect(wrapper.find('input').attributes('id')).toBe('my-input')
  })

  it('sets type attribute', () => {
    const wrapper = mount(TdInput, { props: { type: 'email' } })
    expect(wrapper.find('input').attributes('type')).toBe('email')
  })

  it('emits blur event', async () => {
    const wrapper = mount(TdInput)
    await wrapper.find('input').trigger('blur')
    expect(wrapper.emitted('blur')).toHaveLength(1)
  })

  it('emits focus event', async () => {
    const wrapper = mount(TdInput)
    await wrapper.find('input').trigger('focus')
    expect(wrapper.emitted('focus')).toHaveLength(1)
  })
})
