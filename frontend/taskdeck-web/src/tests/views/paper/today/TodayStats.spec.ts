import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import TodayStats from '../../../../views/paper/today/TodayStats.vue'
import type { DossierStatCard } from '../../../../composables/useTodayDossier'

const STATS: DossierStatCard[] = [
  { id: 'captures-needing-triage', value: 12345, numeric: true, label: 'captures to triage', sub: '', tone: 'ink' },
  { id: 'proposals-pending-review', value: 3, numeric: true, label: 'proposals to review', sub: '', tone: 'ember' },
  { id: 'due-today', value: 11, numeric: true, label: 'due today', sub: '', tone: 'ink' },
  { id: 'blocked', value: 4, numeric: true, label: 'blocked', sub: '', tone: 'applied' },
  { id: 'overdue', value: 2, numeric: true, label: 'overdue', sub: '', tone: 'overdue' },
]

describe('TodayStats', () => {
  it('formats numeric values via Intl.NumberFormat', () => {
    const wrapper = mount(TodayStats, { props: { stats: STATS, locale: 'en-US' } })
    const values = wrapper.findAll('[data-testid="stat-value"]').map(n => n.text())
    expect(values[0]).toBe('12,345')
    expect(values[1]).toBe('3')
    expect(values[2]).toBe('11')
    expect(values[3]).toBe('4')
    expect(values[4]).toBe('2')
  })

  it('respects the provided locale separator', () => {
    const wrapper = mount(TodayStats, {
      props: {
        stats: [{ id: 'captures-needing-triage', value: 12345, numeric: true, label: 'x', sub: '', tone: 'ink' }],
        locale: 'de-DE',
      },
    })
    // de-DE uses '.' as thousands separator.
    expect(wrapper.find('[data-testid="stat-value"]').text()).toBe('12.345')
  })
})
