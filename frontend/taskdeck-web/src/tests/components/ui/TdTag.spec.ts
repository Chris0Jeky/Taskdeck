import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import TdTag from '../../../components/ui/TdTag.vue'

describe('TdTag', () => {
  it('renders slot content as the tag label', () => {
    const wrapper = mount(TdTag, { slots: { default: 'Bug' } })
    expect(wrapper.find('.td-tag__label').text()).toBe('Bug')
  })

  it('does not show remove button by default', () => {
    const wrapper = mount(TdTag, { slots: { default: 'Feature' } })
    expect(wrapper.find('.td-tag__remove').exists()).toBe(false)
  })

  it('shows remove button when removable is true', () => {
    const wrapper = mount(TdTag, {
      props: { removable: true },
      slots: { default: 'Feature' },
    })
    const removeBtn = wrapper.find('.td-tag__remove')
    expect(removeBtn.exists()).toBe(true)
    expect(removeBtn.attributes('aria-label')).toBe('Remove tag')
  })

  it('emits remove event when remove button is clicked', async () => {
    const wrapper = mount(TdTag, {
      props: { removable: true },
      slots: { default: 'Label' },
    })
    await wrapper.find('.td-tag__remove').trigger('click')
    expect(wrapper.emitted('remove')).toHaveLength(1)
  })

  it('applies custom color CSS variable when color prop is set', () => {
    const wrapper = mount(TdTag, {
      props: { color: '#ff6600' },
      slots: { default: 'Orange' },
    })
    expect(wrapper.classes()).toContain('td-tag--custom')
    expect(wrapper.attributes('style')).toContain('--td-tag-color: #ff6600')
  })

  it('does not apply custom class or style when no color is set', () => {
    const wrapper = mount(TdTag, { slots: { default: 'Plain' } })
    expect(wrapper.classes()).not.toContain('td-tag--custom')
    expect(wrapper.attributes('style')).toBeUndefined()
  })

  it('renders as a span element', () => {
    const wrapper = mount(TdTag, { slots: { default: 'Tag' } })
    expect(wrapper.element.tagName).toBe('SPAN')
  })
})
