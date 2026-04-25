import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import TodayStreak from '../../../../views/paper/today/TodayStreak.vue'
import type { DossierStreak } from '../../../../composables/useTodayDossier'

function makeStreak(overrides: Partial<DossierStreak> = {}): DossierStreak {
  // Deterministic intensity: each cell is index modulo 5 -> buckets 0..4
  const cells = Array.from({ length: 90 }, (_, i) => i % 5)
  return {
    cells,
    todayIndex: 89,
    totalDays: 17,
    longestThisYear: 23,
    ...overrides,
  }
}

describe('TodayStreak', () => {
  it('renders 90 cells in the grid', () => {
    const wrapper = mount(TodayStreak, { props: { streak: makeStreak() } })
    const cells = wrapper.findAll('.today-streak__cell')
    expect(cells).toHaveLength(90)
  })

  it('highlights only the today cell', () => {
    const wrapper = mount(TodayStreak, { props: { streak: makeStreak() } })
    const cells = wrapper.findAll('.today-streak__cell')
    const todayCells = cells.filter(c => c.attributes('data-today') === 'true')
    expect(todayCells).toHaveLength(1)
    expect(cells[89].classes()).toContain('today-streak__cell--today')
  })

  it('buckets intensity deterministically based on cell value', () => {
    // Same input → same output, no randomness in the component itself.
    const wrapper1 = mount(TodayStreak, { props: { streak: makeStreak() } })
    const wrapper2 = mount(TodayStreak, { props: { streak: makeStreak() } })
    const buckets1 = wrapper1.findAll('.today-streak__cell').map(c => c.attributes('data-bucket'))
    const buckets2 = wrapper2.findAll('.today-streak__cell').map(c => c.attributes('data-bucket'))
    expect(buckets1).toEqual(buckets2)
    // Spot-check the modulo pattern
    expect(buckets1[0]).toBe('0')
    expect(buckets1[1]).toBe('1')
    expect(buckets1[4]).toBe('4')
    expect(buckets1[5]).toBe('0')
  })
})
