import { describe, expect, it } from 'vitest'
import { mapSimilarPast } from '../../utils/paperReviewSimilarPast'

describe('mapSimilarPast', () => {
  it('preserves row data and maps only an explicit applied verdict to applied', () => {
    expect(mapSimilarPast({
      decisions: [
        { serial: '001', title: 'Applied decision', verdict: 'Applied', date: '2026-09-01' },
        { serial: '002', title: 'Mixed-case decision', verdict: 'aPpLiEd', date: '2026-09-02' },
        { serial: '003', title: 'Rejected decision', verdict: 'Rejected', date: '2026-09-03' },
        { serial: '004', title: 'Unknown decision', verdict: 'future-status', date: '2026-09-04' },
      ],
      applyRate: 0.5,
    })).toEqual([
      { serial: '001', title: 'Applied decision', verdict: 'applied', date: '2026-09-01' },
      { serial: '002', title: 'Mixed-case decision', verdict: 'applied', date: '2026-09-02' },
      { serial: '003', title: 'Rejected decision', verdict: 'rejected', date: '2026-09-03' },
      { serial: '004', title: 'Unknown decision', verdict: 'rejected', date: '2026-09-04' },
    ])
  })
})
