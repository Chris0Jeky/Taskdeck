import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, ref } from 'vue'
import CardEstimateField from '../../components/board/CardEstimateField.vue'
import { formatEstimatedEffort } from '../../utils/estimatedEffort'

function editor() {
  return mount(defineComponent({
    components: { CardEstimateField },
    setup: () => ({ hours: ref(''), minutes: ref('') }),
    template: '<CardEstimateField v-model:hours="hours" v-model:minutes="minutes" />',
  }))
}

describe('CardEstimateField', () => {
  it('distinguishes no estimate from explicit zero, formats hours, and explicitly clears', async () => {
    const wrapper = editor()
    expect(wrapper.get('[data-testid="estimate-summary"]').text()).toContain('Not estimated')
    await wrapper.get('[data-testid="estimate-minutes"]').setValue('0')
    expect(wrapper.get('[data-testid="estimate-summary"]').text()).toBe('0m')
    await wrapper.get('[data-testid="estimate-hours"]').setValue('2')
    await wrapper.get('[data-testid="estimate-minutes"]').setValue('30')
    expect(wrapper.get('[data-testid="estimate-summary"]').text()).toBe('2h 30m')
    await wrapper.get('button').trigger('click')
    expect(wrapper.get('[data-testid="estimate-summary"]').text()).toContain('Not estimated')
    expect(wrapper.find('button').exists()).toBe(false)
  })

  it('shows bad input verbatim with an accessible validation error', async () => {
    const wrapper = editor()
    await wrapper.get('[data-testid="estimate-hours"]').setValue('1.5')
    expect(wrapper.get('[role="alert"]').text()).toContain('whole')
    expect(wrapper.get('[data-testid="estimate-hours"]').attributes('aria-invalid')).toBe('true')
    expect((wrapper.get('[data-testid="estimate-hours"]').element as HTMLInputElement).value).toBe('1.5')
    await wrapper.get('[data-testid="estimate-hours"]').setValue('16666')
    await wrapper.get('[data-testid="estimate-minutes"]').setValue('40')
    expect(wrapper.get('[data-testid="estimate-summary"]').text()).toBe('16666h 40m')
    await wrapper.get('[data-testid="estimate-minutes"]').setValue('41')
    expect(wrapper.get('[role="alert"]').text()).toContain('cannot exceed')
  })

  it.each([['', '', 'Not estimated'], ['0', '0', '0m'], ['2', '30', '2h 30m']])('shows a read-only summary without mutation controls', (hours, minutes, summary) => {
    const wrapper = mount(CardEstimateField, { props: { hours, minutes, readOnly: true } })
    expect(wrapper.text()).toContain(summary)
    expect(wrapper.find('input').exists()).toBe(false)
    expect(wrapper.find('button').exists()).toBe(false)
  })

  it('formats large board totals without applying the individual-card limit', () => {
    expect(formatEstimatedEffort(6_000_001)).toBe('100000h 1m')
  })
})
