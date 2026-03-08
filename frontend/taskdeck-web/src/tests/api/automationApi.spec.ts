import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { automationApi } from '../../api/automationApi'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
  },
}))

describe('automationApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('queries proposals with filters', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: [] })

    await automationApi.getProposals({ status: 'PendingReview', limit: 25 })

    expect(http.get).toHaveBeenCalledWith('/automation/proposals?status=PendingReview&limit=25')
  })

  it('sends idempotency key when executing proposal', async () => {
    vi.mocked(http.post).mockResolvedValue({ data: { id: 'p1' } })

    await automationApi.executeProposal('p1', 'req-1')

    expect(http.post).toHaveBeenCalledWith(
      '/automation/proposals/p1/execute',
      null,
      { headers: { 'Idempotency-Key': 'req-1' } }
    )
  })

  it('passes through null optional rejection reasons', async () => {
    vi.mocked(http.post).mockResolvedValue({ data: { id: 'p1' } })

    await automationApi.rejectProposal('p1', null)

    expect(http.post).toHaveBeenCalledWith('/automation/proposals/p1/reject', { reason: null })
  })
})
