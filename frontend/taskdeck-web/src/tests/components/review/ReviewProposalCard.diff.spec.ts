import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import type { Proposal } from '../../../types/automation'
import type { ReviewDiffMode } from '../../../composables/useReviewActions'
import ReviewProposalCard from '../../../components/review/ReviewProposalCard.vue'

// #1397: PR #1395 made `/diff` 400 for expired/terminal proposals. The Legacy
// card must present the stored preview under a read-only banner (never a live
// diff), a "no stored preview" note when there is nothing stored, and an
// explicit invalid verdict when a proposal has no operations Apply would accept.

function makeProposal(overrides: Partial<Proposal> = {}): Proposal {
  const now = new Date().toISOString()
  return {
    id: 'p-1',
    sourceType: 'Chat',
    sourceReferenceId: null,
    boardId: 'board-1',
    requestedByUserId: 'u-1',
    status: 'PendingReview',
    riskLevel: 'Low',
    summary: 'Test proposal',
    diffPreview: null,
    validationIssues: null,
    createdAt: now,
    updatedAt: now,
    expiresAt: new Date(Date.now() + 60 * 60_000).toISOString(),
    decidedAt: null,
    decidedByUserId: null,
    appliedAt: null,
    failureReason: null,
    correlationId: 'corr-1',
    operations: [],
    ...overrides,
  } as Proposal
}

function mountCard(props: {
  proposal?: Proposal
  isExpired?: boolean
  selectedDiff?: string | null
  selectedDiffMode?: ReviewDiffMode | null
  selectedDiffInvalidReason?: string | null
  selectedDiffRevised?: boolean | null
}) {
  const proposal = props.proposal ?? makeProposal()
  return mount(ReviewProposalCard, {
    props: {
      proposal,
      isExpired: props.isExpired ?? false,
      isBusy: false,
      selectedDiffProposalId: proposal.id,
      selectedDiff: props.selectedDiff ?? null,
      selectedDiffMode: props.selectedDiffMode ?? null,
      selectedDiffInvalidReason: props.selectedDiffInvalidReason ?? null,
      selectedDiffRevised: props.selectedDiffRevised ?? null,
      captureHref: '/workspace/inbox',
      proposalHref: '/workspace/review#proposal-p-1',
    },
  })
}

describe('ReviewProposalCard diff presentation (#1397)', () => {
  it('shows a read-only banner and the stored preview for an expired proposal', () => {
    const wrapper = mountCard({
      proposal: makeProposal({ status: 'Expired' }),
      isExpired: true,
      selectedDiffMode: 'stored',
      selectedDiff: '0. Create card "Archived"',
    })

    const banner = wrapper.find('[data-testid="review-diff-banner"]')
    expect(banner.exists()).toBe(true)
    expect(banner.text()).toContain('Expired')
    expect(banner.text()).toContain('read-only')
    const stored = wrapper.find('[data-testid="review-diff-stored"]')
    expect(stored.exists()).toBe(true)
    expect(stored.text()).toContain('Archived')
  })

  it('shows the banner and a no-stored-preview note when there is no stored content and no operations', () => {
    const wrapper = mountCard({
      proposal: makeProposal({ status: 'Expired' }),
      isExpired: true,
      selectedDiffMode: 'stored',
      selectedDiff: null,
    })

    expect(wrapper.find('[data-testid="review-diff-banner"]').exists()).toBe(true)
    const storedEmpty = wrapper.find('[data-testid="review-diff-stored-empty"]')
    expect(storedEmpty.exists()).toBe(true)
    expect(storedEmpty.text()).toContain('No stored preview')
    // Never falls through to a live "Operation details" pane for a settled proposal.
    expect(wrapper.find('[data-testid="review-diff-pre"]').exists()).toBe(false)
  })

  it('falls back to the recorded operations when no stored preview was captured (#1397 / Codex)', () => {
    // Normal creation flows never populate diffPreview: a read-only proposal
    // that still has operations must render a local operation listing, not a
    // dead "no stored preview" end (and never a live /diff call — prop-driven
    // here, so no network is possible by construction).
    const wrapper = mountCard({
      proposal: makeProposal({
        status: 'Expired',
        operations: [
          {
            id: 'op-2',
            proposalId: 'p-1',
            sequence: 1,
            actionType: 'MoveCard',
            targetType: 'Card',
            targetId: 'card-9',
            parameters: '{}',
            idempotencyKey: 'k-2',
            expectedVersion: null,
          },
          {
            id: 'op-1',
            proposalId: 'p-1',
            sequence: 0,
            actionType: 'CreateCard',
            targetType: 'Card',
            targetId: null,
            parameters: '{}',
            idempotencyKey: 'k-1',
            expectedVersion: null,
          },
        ],
      }),
      isExpired: true,
      selectedDiffMode: 'stored',
      selectedDiff: null,
    })

    expect(wrapper.find('[data-testid="review-diff-stored-ops-note"]').exists()).toBe(true)
    const ops = wrapper.find('[data-testid="review-diff-stored-operations"]')
    expect(ops.exists()).toBe(true)
    // Sequence-ordered: CreateCard (seq 0) before MoveCard (seq 1).
    expect(ops.text()).toMatch(/1\. CreateCard Card[\s\S]*2\. MoveCard Card \(card-9\)/)
    expect(wrapper.find('[data-testid="review-diff-stored-empty"]').exists()).toBe(false)
  })

  it('renders the invalid verdict with the zero-op fallback when no backend reason is supplied', () => {
    const wrapper = mountCard({
      selectedDiffMode: 'invalid',
      selectedDiff: null,
    })

    const invalid = wrapper.find('[data-testid="review-diff-invalid"]')
    expect(invalid.exists()).toBe(true)
    expect(invalid.text()).toContain('no operations')
    expect(invalid.text()).toContain('reject')
  })

  it('renders the backend reason verbatim in the invalid verdict (#1397 MEDIUM-1)', () => {
    // The expiry-race 400 carries "Proposal has expired" — the card must render
    // THAT, never the hardcoded zero-op copy.
    const wrapper = mountCard({
      selectedDiffMode: 'invalid',
      selectedDiff: null,
      selectedDiffInvalidReason: 'Proposal has expired',
    })

    const invalid = wrapper.find('[data-testid="review-diff-invalid"]')
    expect(invalid.exists()).toBe(true)
    expect(invalid.text()).toContain('Proposal has expired')
    expect(invalid.text()).not.toContain('no operations')
  })

  it('discloses a revision on the stored preview (#1397 MEDIUM-2)', () => {
    const wrapper = mountCard({
      proposal: makeProposal({ status: 'Applied' }),
      selectedDiffMode: 'stored',
      selectedDiff: '0. Create card "Original"',
      selectedDiffRevised: true,
    })

    const note = wrapper.find('[data-testid="review-diff-revised-note"]')
    expect(note.exists()).toBe(true)
    expect(note.text()).toContain('revised')
    expect(note.text()).toContain('original')
    // The banner itself already attributes the content to the original submission.
    expect(wrapper.find('[data-testid="review-diff-banner"]').text()).toContain('original submission')
  })

  it('omits the revised note when the revision state is unknown or absent', () => {
    for (const revised of [false, null]) {
      const wrapper = mountCard({
        proposal: makeProposal({ status: 'Expired' }),
        isExpired: true,
        selectedDiffMode: 'stored',
        selectedDiff: 'stored',
        selectedDiffRevised: revised,
      })
      expect(wrapper.find('[data-testid="review-diff-revised-note"]').exists()).toBe(false)
    }
  })

  it('renders the live diff pane for a still-actionable proposal', () => {
    const wrapper = mountCard({
      selectedDiffMode: 'live',
      selectedDiff: '0. Create card "Fix login"',
    })

    const pre = wrapper.find('[data-testid="review-diff-pre"]')
    expect(pre.exists()).toBe(true)
    expect(pre.text()).toContain('Fix login')
    expect(wrapper.find('.td-review-card__diff-label').text()).toBe('Operation details')
    expect(wrapper.find('[data-testid="review-diff-banner"]').exists()).toBe(false)
  })

  it('hides the pane entirely while a live diff is still loading (no premature empty state)', () => {
    const wrapper = mountCard({
      selectedDiffMode: 'live',
      selectedDiff: null,
    })

    // Live mode + no content yet → nothing rendered (mirrors the pre-fetch state),
    // so a slow request never flashes a misleading "no changes" line.
    expect(wrapper.find('[data-testid="review-diff-wrapper"]').exists()).toBe(false)
  })
})
