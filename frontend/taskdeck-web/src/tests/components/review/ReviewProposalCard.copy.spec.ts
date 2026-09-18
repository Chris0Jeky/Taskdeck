import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, RouterLinkStub } from '@vue/test-utils'
import type { Proposal } from '../../../types/automation'
import ReviewProposalCard from '../../../components/review/ReviewProposalCard.vue'
import { resetProposalDisplayNamesForTests } from '../../../composables/useProposalDisplayNames'

const mocks = vi.hoisted(() => ({
  getBoards: vi.fn(),
  getColumns: vi.fn(),
}))

vi.mock('../../../api/boardsApi', () => ({
  boardsApi: { getBoards: mocks.getBoards },
}))

vi.mock('../../../api/columnsApi', () => ({
  columnsApi: { getColumns: mocks.getColumns },
}))

function makeExpiredProposal(): Proposal {
  return {
    id: 'proposal-1',
    sourceType: 'Chat',
    sourceReferenceId: null,
    boardId: 'board-1',
    requestedByUserId: 'user-1',
    status: 'Expired',
    riskLevel: 'Low',
    summary: 'Recorded operation fallback',
    diffPreview: null,
    validationIssues: null,
    createdAt: '2026-09-01T10:00:00.000Z',
    updatedAt: '2026-09-01T10:30:00.000Z',
    expiresAt: '2026-09-01T11:00:00.000Z',
    decidedAt: null,
    decidedByUserId: null,
    appliedAt: null,
    failureReason: null,
    correlationId: 'correlation-1',
    operations: [
      {
        id: 'operation-1',
        proposalId: 'proposal-1',
        sequence: 0,
        actionType: 'CreateCard',
        targetType: 'Card',
        targetId: null,
        parameters: '{}',
        idempotencyKey: 'operation-key-1',
        expectedVersion: null,
      },
    ],
  } as Proposal
}

describe('ReviewProposalCard fallback copy (#1434)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    resetProposalDisplayNamesForTests()
    mocks.getBoards.mockResolvedValue([])
    mocks.getColumns.mockResolvedValue([])
  })

  it('explains a recorded-operation fallback once in the read-only banner', async () => {
    const proposal = makeExpiredProposal()
    const wrapper = mount(ReviewProposalCard, {
      props: {
        proposal,
        isExpired: true,
        isBusy: false,
        selectedDiffProposalId: proposal.id,
        selectedDiff: null,
        selectedDiffMode: 'stored',
        selectedDiffInvalidReason: null,
        selectedDiffRevised: false,
        captureHref: '/workspace/inbox',
        proposalHref: '/workspace/review#proposal-proposal-1',
      },
      global: { stubs: { RouterLink: RouterLinkStub } },
    })

    await flushPromises()

    const banner = wrapper.get('[data-testid="review-diff-banner"]')
    expect(banner.text().toLowerCase()).toContain('no stored preview was captured')
    expect(banner.text().toLowerCase()).toContain('recorded operations')
    expect(wrapper.find('[data-testid="review-diff-stored-ops-note"]').exists()).toBe(false)
    expect(wrapper.get('[data-testid="review-diff-stored-operations"]').text()).toContain(
      'Create Card Card',
    )

    wrapper.unmount()
  })
})
