import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import TdFieldWrapper from '../../../components/ui/TdFieldWrapper.vue'

describe('TdFieldWrapper', () => {
  it('renders label when provided', () => {
    const wrapper = mount(TdFieldWrapper, { props: { label: 'Name' } })
    expect(wrapper.find('.td-field__label').text()).toContain('Name')
  })

  it('does not render label when empty', () => {
    const wrapper = mount(TdFieldWrapper)
    expect(wrapper.find('.td-field__label').exists()).toBe(false)
  })

  it('renders required indicator when required', () => {
    const wrapper = mount(TdFieldWrapper, { props: { label: 'Name', required: true } })
    expect(wrapper.find('.td-field__required').exists()).toBe(true)
    expect(wrapper.find('.td-field__required').text()).toBe('*')
  })

  it('renders error message', () => {
    const wrapper = mount(TdFieldWrapper, { props: { error: 'Required field' } })
    expect(wrapper.find('.td-field__error').text()).toBe('Required field')
    expect(wrapper.find('.td-field__error').attributes('role')).toBe('alert')
  })

  it('renders hint when no error', () => {
    const wrapper = mount(TdFieldWrapper, { props: { hint: 'Optional' } })
    expect(wrapper.find('.td-field__hint').text()).toBe('Optional')
  })

  it('hides hint when error is present', () => {
    const wrapper = mount(TdFieldWrapper, { props: { hint: 'Optional', error: 'Required' } })
    expect(wrapper.find('.td-field__hint').exists()).toBe(false)
    expect(wrapper.find('.td-field__error').exists()).toBe(true)
  })

  it('renders slot content in control area', () => {
    const wrapper = mount(TdFieldWrapper, {
      slots: { default: '<input type="text" />' },
    })
    expect(wrapper.find('.td-field__control input').exists()).toBe(true)
  })

  it('associates label with field via for attribute', () => {
    const wrapper = mount(TdFieldWrapper, { props: { label: 'Email', fieldId: 'email-input' } })
    expect(wrapper.find('label').attributes('for')).toBe('email-input')
  })
})
