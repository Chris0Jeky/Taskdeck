import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import InputAssistField from '../../components/common/InputAssistField.vue'
import type { InputAssistOption } from '../../utils/inputAssist'

const options: InputAssistOption[] = [
  { value: 'health.check', label: 'Health Check' },
]

const cleanups: Array<() => void> = []

function mountField(initialOptions: InputAssistOption[] = []) {
  const host = document.createElement('div')
  const outside = document.createElement('button')
  document.body.append(host, outside)
  const wrapper = mount(InputAssistField, {
    attachTo: host,
    props: { modelValue: '', options: initialOptions },
  })
  cleanups.push(() => {
    wrapper.unmount()
    host.remove()
    outside.remove()
  })
  return { wrapper, input: wrapper.get('input'), outside }
}

describe('InputAssistField late-option focus ownership', () => {
  beforeEach(() => vi.useFakeTimers())

  afterEach(() => {
    for (const cleanup of cleanups.splice(0)) cleanup()
    vi.clearAllTimers()
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  it.each(['health.check', 'Health Check'])(
    'does not select or reclaim focus when %s resolves during the blur delay',
    async (typedValue) => {
      const { wrapper, input, outside } = mountField()
      input.element.focus()
      await input.setValue(typedValue)
      await wrapper.setProps({ modelValue: typedValue })
      const updatesBeforeResponse = wrapper.emitted('update:modelValue')?.length

      outside.focus()
      await nextTick()
      expect(document.activeElement).toBe(outside)
      expect(wrapper.find('[role="listbox"]').exists()).toBe(true)
      const refocus = vi.spyOn(input.element, 'focus')

      await wrapper.setProps({ options })

      expect(wrapper.emitted('select')).toBeUndefined()
      expect(wrapper.emitted('update:modelValue')).toHaveLength(updatesBeforeResponse!)
      expect(refocus).not.toHaveBeenCalled()
      expect(document.activeElement).toBe(outside)
      await vi.advanceTimersByTimeAsync(120)
      expect(wrapper.find('[role="listbox"]').exists()).toBe(false)
    },
  )

  it('still canonicalizes a late label match while the input remains focused', async () => {
    const { wrapper, input } = mountField()
    input.element.focus()
    await input.setValue('Health Check')
    await wrapper.setProps({ modelValue: 'Health Check' })

    await wrapper.setProps({ options })

    expect(wrapper.emitted('select')).toEqual([[options[0]]])
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual(['health.check'])
    expect(document.activeElement).toBe(input.element)
    expect(wrapper.find('[role="listbox"]').exists()).toBe(false)
  })

  it('restores late-option eligibility after an intentional return to the input', async () => {
    const { wrapper, input, outside } = mountField()
    input.element.focus()
    await input.setValue('health.check')
    await wrapper.setProps({ modelValue: 'health.check' })
    outside.focus()
    input.element.focus()
    await nextTick()

    await wrapper.setProps({ options })

    expect(wrapper.emitted('select')).toEqual([[options[0]]])
    expect(document.activeElement).toBe(input.element)
  })

  it('preserves explicit option selection during the blur delay', async () => {
    const { wrapper, input, outside } = mountField(options)
    input.element.focus()
    await nextTick()
    outside.focus()
    await nextTick()

    await wrapper.get('[role="option"]').trigger('mousedown')

    expect(wrapper.emitted('select')).toEqual([[options[0]]])
    expect(wrapper.emitted('update:modelValue')).toEqual([['health.check']])
    expect(document.activeElement).toBe(input.element)
  })
})
