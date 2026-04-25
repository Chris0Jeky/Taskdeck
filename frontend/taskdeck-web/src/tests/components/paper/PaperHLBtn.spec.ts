import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import PaperHLBtn from '../../../components/paper/PaperHLBtn.vue'

describe('PaperHLBtn', () => {
  it('renders the label prop', () => {
    const wrapper = mount(PaperHLBtn, { props: { label: 'Apply' } })
    expect(wrapper.text()).toContain('Apply')
    expect(wrapper.find('button').classes()).toContain('pbtn')
  })

  it('falls back to default slot when label is omitted', () => {
    const wrapper = mount(PaperHLBtn, { slots: { default: 'Open' } })
    expect(wrapper.text()).toContain('Open')
  })

  it.each([
    ['default', null],
    ['primary', 'pbtn-primary'],
    ['ember', 'pbtn-ember'],
    ['ghost', 'pbtn-ghost'],
  ] as const)('maps the %s variant to %s', (variant, expected) => {
    const wrapper = mount(PaperHLBtn, { props: { variant, label: 'x' } })
    const classes = wrapper.find('button').classes()
    if (expected) expect(classes).toContain(expected)
    else expect(classes.some(c => c.startsWith('pbtn-'))).toBe(false)
  })

  it('renders the kbd hint with a divider when provided', () => {
    const wrapper = mount(PaperHLBtn, { props: { label: 'Apply', kbd: '⏎' } })
    expect(wrapper.find('.phlbtn-divider').exists()).toBe(true)
    expect(wrapper.find('kbd.pkbd').text()).toBe('⏎')
  })

  it('does not render a divider when no kbd is supplied', () => {
    const wrapper = mount(PaperHLBtn, { props: { label: 'Apply' } })
    expect(wrapper.find('.phlbtn-divider').exists()).toBe(false)
  })

  it('emits click when pressed', async () => {
    const wrapper = mount(PaperHLBtn, { props: { label: 'Apply' } })
    await wrapper.find('button').trigger('click')
    expect(wrapper.emitted('click')).toHaveLength(1)
  })

  it('does not emit click when disabled', async () => {
    const wrapper = mount(PaperHLBtn, { props: { label: 'Apply', disabled: true } })
    await wrapper.find('button').trigger('click')
    expect(wrapper.emitted('click')).toBeUndefined()
    expect(wrapper.find('button').attributes('disabled')).toBeDefined()
  })

  it('renders the icon slot when provided', () => {
    const wrapper = mount(PaperHLBtn, {
      props: { label: 'Apply' },
      slots: { icon: '<span class="ico" />' },
    })
    expect(wrapper.find('.phlbtn-icon .ico').exists()).toBe(true)
  })
})
