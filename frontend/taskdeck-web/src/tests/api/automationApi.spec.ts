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

  // #1462: the backend puts ApprovedRevisionId on every proposal payload specifically "so clients can
  // detect a pinned revision", but no frontend type declared it, so it was invisible to consumers.
  // These pin the field surviving the wire -> Proposal path, so if a mapping/normalisation step is
  // ever introduced between http and the caller, dropping this field fails here.
  //
  // Scope of the guard, stated precisely: this pins RUNTIME pass-through only. It does NOT pin the
  // interface declaration -- `tsconfig.app.json` excludes `src/tests/**`, so `npm run typecheck` never
  // type-checks this file, and TS types are erased at runtime. Verified by removing the field from
  // `Proposal` and re-running typecheck: it passed. Nothing currently guards the declaration itself,
  // because the field deliberately has no consumer yet (#1462 forbids a UI claim without a separate
  // design decision).
  it('preserves approvedRevisionId on listed proposals', async () => {
    const pinned = 'b3f1c2d4-0000-4000-8000-000000000001'
    vi.mocked(http.get).mockResolvedValue({
      data: [
        { id: 'p1', approvedRevisionId: pinned },
        { id: 'p2', approvedRevisionId: null },
      ],
    })

    const proposals = await automationApi.getProposals()

    expect(proposals[0].approvedRevisionId).toBe(pinned)
    // Explicit null (approved from the original operations) must survive as null, not be dropped to
    // undefined -- the backend serializes the Guid? as null rather than omitting it.
    expect(proposals[1].approvedRevisionId).toBeNull()
  })

  it('preserves approvedRevisionId on a single proposal read', async () => {
    const pinned = 'b3f1c2d4-0000-4000-8000-000000000002'
    vi.mocked(http.get).mockResolvedValue({ data: { id: 'p1', approvedRevisionId: pinned } })

    const proposal = await automationApi.getProposal('p1')

    expect(proposal.approvedRevisionId).toBe(pinned)
  })
})
