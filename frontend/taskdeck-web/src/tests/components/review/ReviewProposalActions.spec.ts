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
    operations: [],
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
})
