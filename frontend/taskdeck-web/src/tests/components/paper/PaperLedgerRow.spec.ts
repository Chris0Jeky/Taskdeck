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
    await wrapper.find('.paper-ledger-row').trigger('click')
    expect(wrapper.emitted('open')).toHaveLength(1)
  })

  it('emits open on Enter / Space', async () => {
    const wrapper = mount(PaperLedgerRow, {
      props: { idx: 1, title: 'Row' },
    })
    const row = wrapper.find('.paper-ledger-row')
    await row.trigger('keydown', { key: 'Enter' })
    await row.trigger('keydown', { key: ' ' })
    expect(wrapper.emitted('open')).toHaveLength(2)
  })

  it('does not emit when not interactive', async () => {
    const wrapper = mount(PaperLedgerRow, {
      props: { idx: 1, title: 'Row', interactive: false },
    })
    const row = wrapper.find('.paper-ledger-row')
    await row.trigger('click')
    await row.trigger('keydown', { key: 'Enter' })
    expect(wrapper.emitted('open')).toBeUndefined()
    expect(row.attributes('role')).toBeUndefined()
  })

  it('does not advertise itself as clickable when interactive=false', () => {
    // The cursor: pointer rule is scoped to the [role='button'] selector,
    // so a non-interactive row should neither carry the role nor a tabindex.
    // Exercising the absence of those attributes guarantees the CSS selector
    // does NOT match, which keeps `cursor: pointer` from misleading users.
    const wrapper = mount(PaperLedgerRow, {
      props: { idx: 1, title: 'Static row', interactive: false },
    })
    const row = wrapper.find('.paper-ledger-row')
    expect(row.attributes('role')).toBeUndefined()
    expect(row.attributes('tabindex')).toBeUndefined()
    expect(wrapper.find('svg[data-icon="chevronRight"]').exists()).toBe(false)
  })

  it('keeps the interactive row addressable as a button', () => {
    const wrapper = mount(PaperLedgerRow, {
      props: { idx: 1, title: 'Active row' },
    })
    const row = wrapper.find('.paper-ledger-row')
    expect(row.attributes('role')).toBe('button')
    expect(row.attributes('tabindex')).toBe('0')
  })
})
