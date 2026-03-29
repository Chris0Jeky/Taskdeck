import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import TdSelect from '../../../components/ui/TdSelect.vue'

describe('TdSelect', () => {
  it('renders a select element', () => {
    const wrapper = mount(TdSelect)
    expect(wrapper.find('select').exists()).toBe(true)
  })

  it('renders placeholder option when provided', () => {
    const wrapper = mount(TdSelect, { props: { placeholder: 'Choose...' } })
    const options = wrapper.findAll('option')
    expect(options[0]?.text()).toBe('Choose...')
    expect(options[0]?.attributes('disabled')).toBeDefined()
  })

  it('renders slot options', () => {
    const wrapper = mount(TdSelect, {
      slots: { default: '<option value="a">A</option><option value="b">B</option>' },
    })
    const options = wrapper.findAll('option')
    expect(options).toHaveLength(2)
  })

  it('emits update:modelValue on change', async () => {
    const wrapper = mount(TdSelect, {
      slots: { default: '<option value="a">A</option><option value="b">B</option>' },
    })
    await wrapper.find('select').setValue('b')
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual(['b'])
  })

  it('applies error class when error is true', () => {
    const wrapper = mount(TdSelect, { props: { error: true } })
    expect(wrapper.find('select').classes()).toContain('td-select--error')
  })

  it('sets disabled attribute', () => {
    const wrapper = mount(TdSelect, { props: { disabled: true } })
    expect(wrapper.find('select').attributes('disabled')).toBeDefined()
  })

  it('renders chevron icon', () => {
    const wrapper = mount(TdSelect)
    expect(wrapper.find('.td-select-chevron').exists()).toBe(true)
  })
})
