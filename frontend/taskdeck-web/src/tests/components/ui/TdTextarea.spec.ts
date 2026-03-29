import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import TdTextarea from '../../../components/ui/TdTextarea.vue'

describe('TdTextarea', () => {
  it('renders a textarea element', () => {
    const wrapper = mount(TdTextarea)
    expect(wrapper.find('textarea').exists()).toBe(true)
  })

  it('binds modelValue', () => {
    const wrapper = mount(TdTextarea, { props: { modelValue: 'hello' } })
    expect(wrapper.find('textarea').element.value).toBe('hello')
  })

  it('emits update:modelValue on input', async () => {
    const wrapper = mount(TdTextarea)
    await wrapper.find('textarea').setValue('test')
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual(['test'])
  })

  it('applies error class when error is true', () => {
    const wrapper = mount(TdTextarea, { props: { error: true } })
    expect(wrapper.find('textarea').classes()).toContain('td-textarea--error')
  })

  it('sets aria-invalid when error is true', () => {
    const wrapper = mount(TdTextarea, { props: { error: true } })
    expect(wrapper.find('textarea').attributes('aria-invalid')).toBe('true')
  })

  it('sets rows attribute', () => {
    const wrapper = mount(TdTextarea, { props: { rows: 6 } })
    expect(wrapper.find('textarea').attributes('rows')).toBe('6')
  })

  it('defaults to 3 rows', () => {
    const wrapper = mount(TdTextarea)
    expect(wrapper.find('textarea').attributes('rows')).toBe('3')
  })

  it('sets disabled attribute', () => {
    const wrapper = mount(TdTextarea, { props: { disabled: true } })
    expect(wrapper.find('textarea').attributes('disabled')).toBeDefined()
  })
})
