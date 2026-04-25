import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import PaperLedgerRow from '../../../components/paper/PaperLedgerRow.vue'

describe('PaperLedgerRow', () => {
  it('renders idx, title, meta and a chevron', () => {
    const wrapper = mount(PaperLedgerRow, {
      props: { idx: 14, title: 'Split dark mode card', meta: 'Apr 25 · 11:42' },
    })
    expect(wrapper.text()).toContain('14')
    expect(wrapper.text()).toContain('Split dark mode card')
    expect(wrapper.text()).toContain('Apr 25 · 11:42')
    expect(wrapper.find('svg[data-icon="chevronRight"]').exists()).toBe(true)
  })

  it('renders a placeholder dash when no status is provided', () => {
    const wrapper = mount(PaperLedgerRow, {
      props: { idx: '01', title: 'Row' },
    })
    expect(wrapper.text()).toContain('—')
  })

  it('renders the status pill when provided', () => {
    const wrapper = mount(PaperLedgerRow, {
      props: {
        idx: 1,
        title: 'Row',
        status: { kind: 'applied', label: 'APPLIED' },
      },
    })
    const pill = wrapper.find('.pstatus')
    expect(pill.exists()).toBe(true)
    expect(pill.text()).toBe('APPLIED')
    expect(pill.classes()).toContain('applied')
  })

  it('emits open on click when interactive', async () => {
    const wrapper = mount(PaperLedgerRow, {
      props: { idx: 1, title: 'Row' },
    })
    await wrapper.trigger('click')
    expect(wrapper.emitted('open')).toHaveLength(1)
  })

  it('emits open on Enter / Space', async () => {
    const wrapper = mount(PaperLedgerRow, {
      props: { idx: 1, title: 'Row' },
    })
    await wrapper.trigger('keydown', { key: 'Enter' })
    await wrapper.trigger('keydown', { key: ' ' })
    expect(wrapper.emitted('open')).toHaveLength(2)
  })

  it('does not emit when not interactive', async () => {
    const wrapper = mount(PaperLedgerRow, {
      props: { idx: 1, title: 'Row', interactive: false },
    })
    await wrapper.trigger('click')
    await wrapper.trigger('keydown', { key: 'Enter' })
    expect(wrapper.emitted('open')).toBeUndefined()
    expect(wrapper.attributes('role')).toBeUndefined()
  })
})
