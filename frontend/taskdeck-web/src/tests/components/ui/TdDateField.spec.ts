import { afterEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { defineComponent, ref } from 'vue'
import { TdDateField } from '../../../components/ui'

const showPickerDescriptor = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'showPicker')
const dateInputPrototype = HTMLInputElement.prototype as unknown as { showPicker?: () => void }

function installShowPicker(showPicker: () => void) {
  Object.defineProperty(HTMLInputElement.prototype, 'showPicker', {
    configurable: true,
    value: showPicker,
  })
}

afterEach(() => {
  if (showPickerDescriptor) {
    Object.defineProperty(HTMLInputElement.prototype, 'showPicker', showPickerDescriptor)
  } else {
    delete dateInputPrototype.showPicker
  }
})

describe('TdDateField', () => {
  it('opens the native picker when any part of the field is clicked', async () => {
    const showPicker = vi.fn()
    installShowPicker(showPicker)
    const wrapper = mount(TdDateField)

    await wrapper.get('input').trigger('click')

    expect(showPicker).toHaveBeenCalledOnce()
  })

  it('keeps normal v-model typing, change listeners, and native attributes', async () => {
    const onChange = vi.fn()
    const Host = defineComponent({
      components: { TdDateField },
      setup() {
        return { dueDate: ref('2026-08-23') }
      },
      template: `
        <TdDateField
          v-model="dueDate"
          class="due-field"
          aria-label="Due date"
          :disabled="false"
          @change="onChange"
        />
      `,
      methods: { onChange },
    })
    const wrapper = mount(Host)
    const input = wrapper.get<HTMLInputElement>('input')

    expect(input.element.value).toBe('2026-08-23')
    expect(input.classes()).toContain('due-field')
    expect(input.attributes('aria-label')).toBe('Due date')
    await input.setValue('2026-08-29')

    expect((wrapper.vm as unknown as { dueDate: string }).dueDate).toBe('2026-08-29')
    expect(onChange).toHaveBeenCalledOnce()
  })

  it('does not open the picker when the field receives focus or Escape', async () => {
    const showPicker = vi.fn()
    const onKeydown = vi.fn()
    installShowPicker(showPicker)
    const wrapper = mount(TdDateField, { attrs: { onKeydown } })
    const input = wrapper.get('input')

    await input.trigger('focus')
    await input.trigger('keydown', { key: 'Escape' })

    expect(showPicker).not.toHaveBeenCalled()
    expect(onKeydown).toHaveBeenCalledOnce()
    expect((onKeydown.mock.calls[0]?.[0] as KeyboardEvent).defaultPrevented).toBe(false)
  })

  it('falls back without an error when showPicker is unsupported', async () => {
    delete dateInputPrototype.showPicker
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})
    const wrapper = mount(TdDateField)

    await expect(wrapper.get('input').trigger('click')).resolves.toBeUndefined()

    expect(consoleError).not.toHaveBeenCalled()
  })

  it('contains a rejected native picker gesture', async () => {
    const showPicker = vi.fn(() => {
      throw new DOMException('User activation is required.', 'NotAllowedError')
    })
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})
    installShowPicker(showPicker)
    const wrapper = mount(TdDateField)

    await expect(wrapper.get('input').trigger('click')).resolves.toBeUndefined()

    expect(showPicker).toHaveBeenCalledOnce()
    expect(consoleError).not.toHaveBeenCalled()
  })
})
