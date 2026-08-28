import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { proposalDeepReviewApi } from '../../api/proposalDeepReviewApi'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
  },
}))

describe('proposalDeepReviewApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('fetches provenance rows for a proposal', async () => {
    const rows = [{ icon: '📄', key: 'card', value: 'test', weight: 'primary' }]
    vi.mocked(http.get).mockResolvedValue({ data: rows })

    const result = await proposalDeepReviewApi.getProvenance('p-1')

    expect(http.get).toHaveBeenCalledWith('/automation/proposals/p-1/provenance', { signal: undefined })
    expect(result).toEqual(rows)
  })

  it('fetches confidence breakdown for a proposal', async () => {
    const breakdown = {
      overall: 0.84,
      components: [{ key: 'Operation 1: create card', value: 0.84 }],
      note: null,
      threshold: null,
      source: 'model-reported' as const,
      meetsThreshold: null,
    }
    vi.mocked(http.get).mockResolvedValue({ data: breakdown })

    const result = await proposalDeepReviewApi.getConfidence('p-1')

    expect(http.get).toHaveBeenCalledWith('/automation/proposals/p-1/confidence', { signal: undefined })
    expect(result).toEqual(breakdown)
  })

  it('fetches side effects for a proposal', async () => {
    const effects = { rows: [], reversibility: { summary: 's', description: 'd', windowMs: 1000 } }
    vi.mocked(http.get).mockResolvedValue({ data: effects })

    const result = await proposalDeepReviewApi.getSideEffects('p-1')

    expect(http.get).toHaveBeenCalledWith('/automation/proposals/p-1/side-effects', { signal: undefined })
    expect(result).toEqual(effects)
  })

  it('fetches conflicts for a proposal', async () => {
    const conflicts = JSON.parse('[{"tone":0,"key":"stale","value":"desc"}]')
    vi.mocked(http.get).mockResolvedValue({ data: conflicts })

    const result = await proposalDeepReviewApi.getConflicts('p-1')

    expect(http.get).toHaveBeenCalledWith('/automation/proposals/p-1/conflicts', { signal: undefined })
    expect(result).toEqual(conflicts)
  })

  it('fetches card history for a proposal', async () => {
    const history = JSON.parse('[{"serial":"#1","event":"created","age":"2h","status":1}]')
    vi.mocked(http.get).mockResolvedValue({ data: history })

    const result = await proposalDeepReviewApi.getHistory('p-1')

    expect(http.get).toHaveBeenCalledWith('/automation/proposals/p-1/history', { signal: undefined })
    expect(result).toEqual(history)
  })

  it('fetches similar past decisions for a proposal', async () => {
    const similar = { decisions: [], applyRate: 0.67 }
    vi.mocked(http.get).mockResolvedValue({ data: similar })

    const result = await proposalDeepReviewApi.getSimilarPast('p-1')

    expect(http.get).toHaveBeenCalledWith('/automation/proposals/p-1/similar-past', { signal: undefined })
    expect(result).toEqual(similar)
  })

  it('encodes special characters in proposal IDs', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: [] })

    await proposalDeepReviewApi.getProvenance('id/with spaces')

    expect(http.get).toHaveBeenCalledWith('/automation/proposals/id%2Fwith%20spaces/provenance', { signal: undefined })
  })

  it('propagates HTTP errors', async () => {
    vi.mocked(http.get).mockRejectedValue(new Error('Network error'))

    await expect(proposalDeepReviewApi.getConfidence('p-1')).rejects.toThrow('Network error')
  })
})
