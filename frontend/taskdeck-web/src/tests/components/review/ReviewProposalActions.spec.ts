import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import type { Proposal } from '../../../types/automation'
import ReviewProposalActions from '../../../components/review/ReviewProposalActions.vue'

// Locks the Legacy action gating to the shared review rules (ADR-0038 / #1124
// drift class). Approve/Reject must only be enabled for a live PendingReview
// proposal; Apply-to-board only for a live Approved proposal; an expired
// proposal swaps to the dismiss affordance and never exposes apply/reject.

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
    // One operation by default: a zero-op proposal is structurally invalid for
    // Approve (#1397), so the actionability tests use a realistic applyable
    // fixture and the zero-op case is asserted explicitly below.
    operations: [
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
    ...overrides,
  } as Proposal
}

function mountActions(props: Partial<{
  proposal: Proposal
  isExpired: boolean
  isBusy: boolean
  selectedDiffProposalId: string | null
}> = {}) {
  return mount(ReviewProposalActions, {
    props: {
      proposal: props.proposal ?? makeProposal(),
      isExpired: props.isExpired ?? false,
      isBusy: props.isBusy ?? false,
      selectedDiffProposalId: props.selectedDiffProposalId ?? null,
    },
  })
}

function findButton(wrapper: ReturnType<typeof mountActions>, label: string) {
  const button = wrapper.findAll('button').find((b) => b.text() === label)
  expect(button, `expected a "${label}" button`).toBeTruthy()
  return button!
}

function isDisabled(wrapper: ReturnType<typeof mountActions>, label: string): boolean {
  return findButton(wrapper, label).attributes('disabled') !== undefined
}

describe('ReviewProposalActions gating', () => {
  it('enables approve and reject for a live PendingReview proposal, disables execute', () => {
    const wrapper = mountActions({ proposal: makeProposal({ status: 'PendingReview' }) })
    expect(isDisabled(wrapper, 'Approve for board')).toBe(false)
    expect(isDisabled(wrapper, 'Reject')).toBe(false)
    expect(isDisabled(wrapper, 'Apply to board')).toBe(true)
  })

  it('enables only execute for a live Approved proposal', () => {
    const wrapper = mountActions({ proposal: makeProposal({ status: 'Approved' }) })
    expect(isDisabled(wrapper, 'Approve for board')).toBe(true)
    expect(isDisabled(wrapper, 'Reject')).toBe(true)
    expect(isDisabled(wrapper, 'Apply to board')).toBe(false)
  })

  it('shows the dismiss affordance and hides apply/reject when expired (#1124)', () => {
    const wrapper = mountActions({
      proposal: makeProposal({ status: 'Approved' }),
      isExpired: true,
    })
    expect(wrapper.text()).toContain('This proposal has expired and can no longer be applied.')
    expect(wrapper.findAll('button').some((b) => b.text() === 'Dismiss')).toBe(true)
    expect(wrapper.findAll('button').some((b) => b.text() === 'Approve for board')).toBe(false)
    expect(wrapper.findAll('button').some((b) => b.text() === 'Apply to board')).toBe(false)
    expect(wrapper.findAll('button').some((b) => b.text() === 'Reject')).toBe(false)
  })

  it('labels the diff button as a stored preview on the expired path (#1397)', () => {
    // Expired proposals no longer offer a live "View Diff" (which now 400s); the
    // button reveals the stored preview instead, so its label must say so.
    const wrapper = mountActions({
      proposal: makeProposal({ status: 'Expired' }),
      isExpired: true,
    })
    expect(wrapper.findAll('button').some((b) => b.text() === 'View stored preview')).toBe(true)
    expect(wrapper.findAll('button').some((b) => b.text() === 'View Diff')).toBe(false)
  })

  it('toggles the stored-preview label when the expired proposal diff is open (#1397)', () => {
    const proposal = makeProposal({ status: 'Expired' })
    const wrapper = mountActions({
      proposal,
      isExpired: true,
      selectedDiffProposalId: proposal.id,
    })
    expect(wrapper.findAll('button').some((b) => b.text() === 'Hide stored preview')).toBe(true)
  })

  it('disables every transition button while busy', () => {
    const wrapper = mountActions({
      proposal: makeProposal({ status: 'PendingReview' }),
      isBusy: true,
    })
    expect(isDisabled(wrapper, 'Approve for board')).toBe(true)
    expect(isDisabled(wrapper, 'Reject')).toBe(true)
    expect(isDisabled(wrapper, 'Apply to board')).toBe(true)
  })

  it('disables apply/reject/execute for terminal statuses', () => {
    for (const status of ['Applied', 'Rejected', 'Failed'] as const) {
      const wrapper = mountActions({ proposal: makeProposal({ status }) })
      expect(isDisabled(wrapper, 'Approve for board')).toBe(true)
      expect(isDisabled(wrapper, 'Reject')).toBe(true)
      expect(isDisabled(wrapper, 'Apply to board')).toBe(true)
    }
  })

  it('disables Approve for a zero-operation pending proposal (#1397 LOW-3)', () => {
    // Apply (and /diff) reject a zero-op proposal with 400; offering Approve
    // only defers that failure past the reviewer's decision. Reject stays
    // available so the reviewer can still clear it.
    const wrapper = mountActions({
      proposal: makeProposal({ status: 'PendingReview', operations: [] }),
    })
    expect(isDisabled(wrapper, 'Approve for board')).toBe(true)
    expect(isDisabled(wrapper, 'Reject')).toBe(false)
  })

  it('labels the diff button as a stored preview for terminal non-expired statuses (#1397 LOW-4)', () => {
    // Applied/Rejected/Failed proposals (visible via "show completed") render the
    // stored preview too — the label must follow the read-only classification,
    // not just expiry.
    for (const status of ['Applied', 'Rejected', 'Failed'] as const) {
      const wrapper = mountActions({ proposal: makeProposal({ status }) })
      expect(wrapper.findAll('button').some((b) => b.text() === 'View stored preview')).toBe(true)
      expect(wrapper.findAll('button').some((b) => b.text() === 'View Diff')).toBe(false)
    }
    // A live pending proposal keeps the live-diff label.
    const live = mountActions({ proposal: makeProposal({ status: 'PendingReview' }) })
    expect(live.findAll('button').some((b) => b.text() === 'View Diff')).toBe(true)
  })
})
