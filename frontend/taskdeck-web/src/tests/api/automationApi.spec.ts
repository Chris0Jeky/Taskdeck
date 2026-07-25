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

  // #1462: the backend carries ApprovedRevisionId on every REST proposal payload, but no frontend type
  // declared it, so it was invisible to consumers. These assert it is not dropped between the response
  // body and the caller, on the reads AND on the decide responses (which is where the pin is born).
  //
  // Scope of the guard, stated precisely so it is not mistaken for more than it is:
  //  - RUNTIME pass-through only. It does NOT pin the interface declaration -- `tsconfig.app.json`
  //    excludes `src/tests/**`, so `npm run typecheck` never type-checks this file. The declaration is
  //    held instead by the exported `ProposalApprovedRevisionId` alias in `types/automation.ts`, which
  //    does live in typechecked source. Reproducible on this tree: delete the interface member and
  //    typecheck fails at the ALIAS (TS2339), never here; delete the member AND the alias and
  //    typecheck passes with this spec untouched and green -- which is exactly how far the protection
  //    in this file reaches. (#1468 tracks the general specs-are-not-type-checked gap.)
  //  - Not "surviving deserialization": `http` is mocked wholesale, so the wire KEY casing is not
  //    exercised here. A serializer naming-policy flip would break every field and surface elsewhere.
  //  - The realistic regression is not a whitelist mapper -- this codebase's api normalizers are
  //    spread-based (`agentApi.ts`, `integrationsApi.ts`), which preserve unknown fields. What these
  //    catch is a field-by-field rebuild or a `?? undefined` narrowing added to one path only.
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

  it('preserves an explicit null approvedRevisionId on a single proposal read', async () => {
    // The null case needs its own assertion on this path: a `?? undefined` narrowing added to
    // getProposal alone would keep every other test green while destroying the null-vs-absent
    // distinction on the read the backend treats as authoritative for effective content.
    vi.mocked(http.get).mockResolvedValue({ data: { id: 'p1', approvedRevisionId: null } })

    const proposal = await automationApi.getProposal('p1')

    expect(proposal.approvedRevisionId).toBeNull()
  })

  // The pin is BORN on the decide responses -- ApproveProposalAsync is what writes it -- so a
  // normalisation added to those responses only would lose it exactly where a UI would read it
  // freshest, while both read-path tests above stayed green.
  it.each([
    ['approveProposal', () => automationApi.approveProposal('p1')],
    ['rejectProposal', () => automationApi.rejectProposal('p1', null)],
    ['executeProposal', () => automationApi.executeProposal('p1', 'req-1')],
  ])('preserves approvedRevisionId on the %s response', async (_name, call) => {
    const pinned = 'b3f1c2d4-0000-4000-8000-000000000003'
    vi.mocked(http.post).mockResolvedValue({ data: { id: 'p1', approvedRevisionId: pinned } })

    const proposal = await call()

    expect(proposal.approvedRevisionId).toBe(pinned)
  })
})
