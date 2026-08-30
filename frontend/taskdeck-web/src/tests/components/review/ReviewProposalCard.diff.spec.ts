import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, RouterLinkStub } from '@vue/test-utils'
import type { Proposal } from '../../../types/automation'
import type { ReviewDiffMode } from '../../../composables/useReviewActions'
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
  selectedDiffProposalId?: string | null
  selectedDiff?: string | null
  selectedDiffMode?: ReviewDiffMode | null
  selectedDiffInvalidReason?: string | null
  selectedDiffRevised?: boolean | null
  readOnly?: boolean
}) {
  const proposal = props.proposal ?? makeProposal()
  return mount(ReviewProposalCard, {
    props: {
      proposal,
      isExpired: props.isExpired ?? false,
      isBusy: false,
      selectedDiffProposalId: props.selectedDiffProposalId ?? proposal.id,
      selectedDiff: props.selectedDiff ?? null,
      selectedDiffMode: props.selectedDiffMode ?? null,
      selectedDiffInvalidReason: props.selectedDiffInvalidReason ?? null,
      selectedDiffRevised: props.selectedDiffRevised ?? null,
      captureHref: '/workspace/inbox',
      proposalHref: '/workspace/review#proposal-p-1',
      readOnly: props.readOnly ?? false,
    },
    global: { stubs: { RouterLink: RouterLinkStub } },
  })
}

describe('ReviewProposalCard diff presentation (#1397)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    resetProposalDisplayNamesForTests()
    mocks.getBoards.mockResolvedValue([])
    mocks.getColumns.mockResolvedValue([])
  })

  // Regression for the third boundary escape found on #1973's read-only surface.
  // `readOnly` reached the action footer but not the details panel, whose links
  // dropdown carries Open Board. `BoardView` / `PaperBoardView` do not gate on
  // `isArchived` and no service rejects a write to an archived board, so that
  // control handed the user a fully editable board — two clicks from an Archive
  // row, off a page whose own copy says to restore the board first.
  // The Links dropdown lives inside the collapsed "Technical details" section,
  // which itself only renders when the proposal has provenance context.
  async function clickButtonContaining(
    wrapper: ReturnType<typeof mountCard>,
    label: string,
  ) {
    const trigger = wrapper
      .findAll('button')
      .find((button) => button.text().includes(label))
    expect(trigger, `expected a button containing "${label}"`).toBeDefined()
    await trigger!.trigger('click')
    await flushPromises()
  }

  async function openLinksDropdown(wrapper: ReturnType<typeof mountCard>) {
    await clickButtonContaining(wrapper, 'Technical details')
    await clickButtonContaining(wrapper, 'Links')
  }

  function archivedHistoryProposal(boardId: string) {
    return makeProposal({
      status: 'Applied',
      boardId,
      sourceType: 'Queue',
      sourceReferenceId: 'capture-1',
      correlationId: 'corr-archived',
    })
  }

  it('withholds Open Board in archived decision history', async () => {
    const wrapper = mountCard({
      proposal: archivedHistoryProposal('archived-board'),
      readOnly: true,
    })
    await flushPromises()
    await openLinksDropdown(wrapper)

    const dropdown = wrapper.find('.td-review-card__links-dropdown')
    expect(dropdown.exists()).toBe(true)
    expect(wrapper.find('[data-testid="review-open-board"]').exists()).toBe(false)
    expect(dropdown.text()).not.toContain('Open Board')
    // The capture and review links are reads and must survive — only the
    // editable destination goes.
    expect(dropdown.text()).toContain('Open Capture')
    expect(dropdown.text()).toContain('Review Link')
  })

  it('keeps Open Board on the live review queue', async () => {
    const wrapper = mountCard({
      proposal: archivedHistoryProposal('live-board'),
      readOnly: false,
    })
    await flushPromises()
    await openLinksDropdown(wrapper)

    expect(wrapper.find('[data-testid="review-open-board"]').exists()).toBe(true)
  })

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
    expect(ops.text()).toMatch(/1\. CreateCard Card[\s\S]*2\. MoveCard Card/)
    expect(ops.text()).not.toContain('card-9')
    expect(wrapper.find('[data-testid="review-diff-stored-empty"]').exists()).toBe(false)
  })

  it('keeps the backend CreateCard headline when board and column ids are present', async () => {
    const wrapper = mountCard({
      proposal: makeProposal({
        operations: [{
          id: 'op-1',
          proposalId: 'p-1',
          sequence: 0,
          actionType: 'CreateCard',
          targetType: 'Card',
          targetId: null,
          parameters: JSON.stringify({ boardId: 'board-1', columnId: 'column-1', title: 'Implement OAuth' }),
          idempotencyKey: 'k-1',
          expectedVersion: null,
        }],
        presentation: {
          plainSummary: 'Add OAuth support',
          impactSummary: 'Adds one card.',
          riskCue: 'Low risk',
          sourceCue: 'Chat',
          operationHeadlines: ['Create card “Implement OAuth” in the authentication column'],
          affectedEntities: [],
        },
      }),
    })

    const plannedChanges = wrapper.findAll('button').find((button) => button.text().includes('Planned changes'))
    expect(plannedChanges).toBeDefined()
    await plannedChanges!.trigger('click')

    expect(wrapper.find('.td-review-card__operation-list').text())
      .toContain('Create card “Implement OAuth” in the authentication column')
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

  it('discloses a revision on the recorded-operations fallback, not as a "stored preview" (#1397 MEDIUM-2 / #1414)', () => {
    // A revised read-only proposal with no captured preview renders the
    // recorded-operations fallback — so the disclosure must be worded for that,
    // never claim a "stored preview" that does not exist.
    const wrapper = mountCard({
      proposal: makeProposal({
        status: 'Expired',
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
      }),
      isExpired: true,
      selectedDiffMode: 'stored',
      selectedDiff: null,
      selectedDiffRevised: true,
    })

    const note = wrapper.find('[data-testid="review-diff-revised-note"]')
    expect(note.exists()).toBe(true)
    expect(note.text()).toContain('revised')
    expect(note.text()).toContain('recorded operations')
    expect(note.text()).not.toContain('stored preview')
    // The recorded-operations fallback is what's on screen (not a stored pre).
    expect(wrapper.find('[data-testid="review-diff-stored-operations"]').exists()).toBe(true)
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

  it('renders the live diff pane when only the proposal ID casing differs', () => {
    const proposal = makeProposal({ id: 'a1b2c3d4-e5f6-47a8-9abc-def012345678' })
    const wrapper = mountCard({
      proposal,
      selectedDiffProposalId: 'A1B2C3D4-E5F6-47A8-9ABC-DEF012345678',
      selectedDiffMode: 'live',
      selectedDiff: '0. Create card "Case test"',
    })

    const wrapperWrapper = wrapper.find('[data-testid="review-diff-wrapper"]')
    expect(wrapperWrapper.exists()).toBe(true)
    expect(wrapper.find('[data-testid="review-diff-pre"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="review-diff-pre"]').text()).toContain('Case test')
    expect(wrapper.find('[data-testid="review-diff-invalid"]').exists()).toBe(false)
  })

  it('does not render any diff pane for unrelated proposal IDs', () => {
    const proposal = makeProposal({ id: 'a1b2c3d4-e5f6-47a8-9abc-def012345678' })
    const wrapper = mountCard({
      proposal,
      selectedDiffProposalId: '11111111-2222-3333-4444-555555555555',
      selectedDiffMode: 'live',
      selectedDiff: '0. Create card "Different"',
    })

    expect(wrapper.find('[data-testid="review-diff-wrapper"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="review-diff-pre"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="review-diff-invalid"]').exists()).toBe(false)
    // Preserve proposal.id and DOM/API identifiers as-is; no normalization.
    expect(wrapper.find('article[id^="proposal-"]').attributes('id')).toBe('proposal-a1b2c3d4-e5f6-47a8-9abc-def012345678')
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

  it('presents readable board and column names in Legacy while disclosing raw IDs', async () => {
    mocks.getBoards.mockResolvedValue([{ id: 'board-1', name: 'Support Triage' }])
    mocks.getColumns.mockResolvedValue([{ id: 'column-1', boardId: 'board-1', name: 'Done' }])
    const wrapper = mountCard({
      proposal: makeProposal({
        operations: [{
          id: 'op-column',
          proposalId: 'p-1',
          sequence: 0,
          actionType: 'MoveCard',
          targetType: 'Column',
          targetId: 'column-1',
          parameters: JSON.stringify({ boardId: 'board-1', columnId: 'column-1' }),
          idempotencyKey: 'k-1',
          expectedVersion: null,
        }],
      }),
      selectedDiffMode: 'stored',
      selectedDiff: null,
    })
    await flushPromises()

    const plannedToggle = wrapper.findAll('.td-review-card__collapse-toggle')
      .find((button) => button.text().includes('Planned changes'))!
    await plannedToggle.trigger('click')
    const planned = wrapper.find('.td-review-card__operation-list')
    expect(wrapper.text()).toContain('Board: Support Triage')
    expect(planned.text()).toContain('Done')
    expect(planned.text()).not.toContain('board-1')
    expect(planned.text()).not.toContain('column-1')
    expect(mocks.getBoards).toHaveBeenCalledTimes(1)
    expect(mocks.getColumns).toHaveBeenCalledTimes(1)

    const details = wrapper.find('[data-testid="review-technical-details"]')
    expect(details.attributes('open')).toBeUndefined()
    await details.find('summary').trigger('click')
    expect(details.text()).toContain('board-1')
    expect(details.text()).toContain('column-1')
  })
})
