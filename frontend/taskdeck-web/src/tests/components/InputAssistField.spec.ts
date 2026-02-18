import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import InputAssistField from '../../components/common/InputAssistField.vue'
import type { InputAssistOption } from '../../utils/inputAssist'

const options: InputAssistOption[] = [
  {
    value: 'health.check',
    label: 'Health Check',
    helperText: 'OpsAdmin role',
    keywords: ['health'],
  },
  {
    value: 'logs.query',
    label: 'Query Logs',
    helperText: 'OpsReader role',
    keywords: ['logs'],
  },
]

describe('InputAssistField', () => {
  it('supports keyboard navigation and enter selection', async () => {
    const wrapper = mount(InputAssistField, {
      props: {
        modelValue: '',
        options,
        ariaLabel: 'Command template',
      },
    })

    const input = wrapper.get('input')
    await input.trigger('focus')
    await input.trigger('keydown', { key: 'ArrowDown' })

    const renderedOptions = wrapper.findAll('[role="option"]')
    expect(renderedOptions).toHaveLength(2)
    expect(renderedOptions[1]?.attributes('aria-selected')).toBe('true')

    await input.trigger('keydown', { key: 'Enter' })

    expect(wrapper.emitted('update:modelValue')).toEqual([[ 'logs.query' ]])
    expect(wrapper.find('[role="listbox"]').exists()).toBe(false)
  })

  it('renders no-results state when query has no matches', async () => {
    const wrapper = mount(InputAssistField, {
      props: {
        modelValue: 'missing-value',
        options,
        noResultsText: 'Nothing found',
      },
    })

    await wrapper.get('input').trigger('focus')

    expect(wrapper.get('.td-input-assist__empty').text()).toContain('Nothing found')
  })

  it('closes the suggestion panel with escape', async () => {
    const wrapper = mount(InputAssistField, {
      props: {
        modelValue: '',
        options,
      },
    })

    const input = wrapper.get('input')
    await input.trigger('focus')
    expect(wrapper.find('[role="listbox"]').exists()).toBe(true)

    await input.trigger('keydown', { key: 'Escape' })
    expect(wrapper.find('[role="listbox"]').exists()).toBe(false)
  })

  it('closes the suggestion panel when input exactly matches an option value', async () => {
    const wrapper = mount(InputAssistField, {
      props: {
        modelValue: '',
        options,
      },
    })

    const input = wrapper.get('input')
    await input.trigger('focus')
    expect(wrapper.find('[role="listbox"]').exists()).toBe(true)

    await input.setValue('health.check')

    expect(wrapper.find('[role="listbox"]').exists()).toBe(false)
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual(['health.check'])
  })

  it('maps exact label match to canonical option value and closes panel', async () => {
    const wrapper = mount(InputAssistField, {
      props: {
        modelValue: '',
        options,
      },
    })

    const input = wrapper.get('input')
    await input.trigger('focus')
    expect(wrapper.find('[role="listbox"]').exists()).toBe(true)

    await input.setValue('Health Check')

    expect(wrapper.find('[role="listbox"]').exists()).toBe(false)
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual(['health.check'])
    expect(wrapper.emitted('select')?.at(-1)?.[0]).toMatchObject({ value: 'health.check', label: 'Health Check' })
  })
})
