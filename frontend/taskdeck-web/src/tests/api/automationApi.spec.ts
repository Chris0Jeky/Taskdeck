import { beforeEach, describe, expect, expectTypeOf, it, vi } from 'vitest'
import http from '../../api/http'
import { automationApi } from '../../api/automationApi'
import type { Proposal } from '../../types/automation'

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
  //  - The `it()` blocks below are RUNTIME pass-through only; they do not pin the interface
  //    declaration. Since #1468 this file IS type-checked (`tsconfig.vitest.json`), so the
  //    declaration is now pinned here too -- by the `expectTypeOf` assertions directly beneath this
  //    comment, not by the runtime tests. The exported `ProposalApprovedRevisionId` alias in
  //    `types/automation.ts` remains the belt to this braces: it holds the field from inside
  //    production source, which is what stops a dead-code sweep even if this file were quarantined.
  //  - Not "surviving deserialization": `http` is mocked wholesale, so the wire KEY casing is not
  //    exercised here. A serializer naming-policy flip would break every field and surface elsewhere.
  //  - The realistic regression is not a whitelist mapper -- this codebase's api normalizers are
  //    spread-based (`agentApi.ts`, `integrationsApi.ts`), which preserve unknown fields. What these
  //    catch is a field-by-field rebuild or a `?? undefined` narrowing added to one path only.

  // Type-level pin (#1468 acceptance criterion). `expectTypeOf` erases at runtime -- this `it` block
  // asserts nothing when vitest runs it; the assertion is discharged by `vue-tsc -b` because this
  // file is now inside `tsconfig.vitest.json`. Mutation-verified, and the two lines do NOT
  // discriminate on the same thing:
  //  - `toEqualTypeOf` (first line) is the load-bearing one. It fails on all three mutations:
  //    deleting the member (TS2339 on the indexed access), widening it to `?:` (TS2344,
  //    `Actual: undefined`), and dropping its nullability (TS2344, `Actual: never`).
  //  - `toHaveProperty` (second line) fires ONLY on deletion (TS2345). An optional property still
  //    satisfies it, so it does not catch the `?:` widening. It is kept because it names the
  //    property explicitly, which is what makes the intent legible at the failure site.
  it('declares approvedRevisionId as a required, nullable string on Proposal', () => {
    expectTypeOf<Proposal['approvedRevisionId']>().toEqualTypeOf<string | null>()
    expectTypeOf<Proposal>().toHaveProperty('approvedRevisionId')
  })

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
