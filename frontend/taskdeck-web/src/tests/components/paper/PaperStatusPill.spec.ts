import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import PaperStatusPill from '../../../components/paper/PaperStatusPill.vue'

describe('PaperStatusPill', () => {
  it('renders the slot content', () => {
    const wrapper = mount(PaperStatusPill, {
      props: { kind: 'live' },
      slots: { default: 'LIVE' },
    })
    expect(wrapper.text()).toBe('LIVE')
    expect(wrapper.classes()).toContain('pstatus')
  })

  it.each(['proposed', 'applied', 'overdue', 'draft', 'live'] as const)(
    'applies %s kind class',
    kind => {
      const wrapper = mount(PaperStatusPill, {
        props: { kind },
        slots: { default: 'X' },
      })
      expect(wrapper.classes()).toContain(kind)
      expect(wrapper.classes()).toContain(`pstatus--${kind}`)
      expect(wrapper.attributes('data-kind')).toBe(kind)
    },
  )

  it('defaults to draft', () => {
    const wrapper = mount(PaperStatusPill, { slots: { default: 'X' } })
    expect(wrapper.classes()).toContain('draft')
  })
})
