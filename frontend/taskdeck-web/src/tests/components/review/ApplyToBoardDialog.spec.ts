import { afterEach, describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ApplyToBoardDialog from '../../../components/review/ApplyToBoardDialog.vue'
import type { Proposal } from '../../../types/automation'

function makeProposal(overrides: Partial<Proposal> = {}): Proposal {
  return {
    id: 'p-1',
    sourceType: 'Queue',
    sourceReferenceId: null,
    boardId: 'b-1',
    requestedByUserId: 'u-1',
    status: 'Approved',
    riskLevel: 'Low',
    summary: 'Raw summary from the backend',
    diffPreview: null,
    validationIssues: null,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
    decidedAt: null,
    decidedByUserId: null,
    appliedAt: null,
    failureReason: null,
    correlationId: 'corr-1',
    operations: [],
    approvedRevisionId: null,
    ...overrides,
  } as Proposal
}

// TdDialog teleports to <body>, so a wrapper left mounted would leak its DOM
// into the next test's body queries. Track and unmount every mount.
let mounted: Array<{ unmount: () => void }> = []

function mountDialog(proposal: Proposal | null, busy = false) {
  const wrapper = mount(ApplyToBoardDialog, {
    props: { proposal, busy },
    attachTo: document.body,
  })
  mounted.push(wrapper)
  return wrapper
}

afterEach(() => {
  mounted.forEach((wrapper) => wrapper.unmount())
  mounted = []
  document.body.innerHTML = ''
})

describe('ApplyToBoardDialog', () => {
  it('renders nothing when there is no proposal awaiting confirmation', () => {
    mountDialog(null)
    expect(document.body.querySelector('[data-testid="apply-confirm-dialog"]')).toBeNull()
  })

  it('carries the proposal summary so the user confirms what will be written', () => {
    mountDialog(
      makeProposal({
        presentation: {
          plainSummary: 'Split “dark mode” into 3 cards',
          impactSummary: '',
          riskCue: '',
          sourceCue: '',
          operationHeadlines: [],
          affectedEntities: [],
        },
      }),
    )
    const dialog = document.body.querySelector('[data-testid="apply-confirm-dialog"]')
    expect(dialog).not.toBeNull()
    expect(
      document.body.querySelector('[data-testid="apply-confirm-summary"]')?.textContent,
    ).toContain('Split “dark mode” into 3 cards')
  })

  it('falls back to the raw summary when there is no presentation summary', () => {
    mountDialog(makeProposal())
    expect(
      document.body.querySelector('[data-testid="apply-confirm-summary"]')?.textContent,
    ).toContain('Raw summary from the backend')
  })

  it('states that the board has not been written to yet', () => {
    mountDialog(makeProposal())
    const dialog = document.body.querySelector('[data-testid="apply-confirm-dialog"]')
    expect(dialog?.textContent).toContain('second and final step')
    expect(dialog?.textContent).toContain('Nothing has been written to the board yet')
  })

  it('pluralizes the operation count it is about to apply', () => {
    mountDialog(
      makeProposal({
        operations: [
          { id: 'o-1' } as Proposal['operations'][number],
          { id: 'o-2' } as Proposal['operations'][number],
        ],
      }),
    )
    expect(
      document.body.querySelector('[data-testid="apply-confirm-operations"]')?.textContent,
    ).toContain('2 operations will be applied')
  })

  it('emits confirm only from the accept button', async () => {
    const wrapper = mountDialog(makeProposal())
    const accept = document.body.querySelector(
      '[data-testid="apply-confirm-accept"]',
    ) as HTMLButtonElement
    accept.click()
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('confirm')).toHaveLength(1)
    expect(wrapper.emitted('cancel')).toBeUndefined()
  })

  it('emits cancel from the cancel button', async () => {
    const wrapper = mountDialog(makeProposal())
    const cancel = document.body.querySelector(
      '[data-testid="apply-confirm-cancel"]',
    ) as HTMLButtonElement
    cancel.click()
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('cancel')).toHaveLength(1)
    expect(wrapper.emitted('confirm')).toBeUndefined()
  })

  it('disables the accept button while the execute call is in flight', () => {
    mountDialog(makeProposal(), true)
    const accept = document.body.querySelector(
      '[data-testid="apply-confirm-accept"]',
    ) as HTMLButtonElement
    expect(accept.disabled).toBe(true)
  })
})
