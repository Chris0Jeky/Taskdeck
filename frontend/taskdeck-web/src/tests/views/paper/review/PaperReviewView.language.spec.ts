import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount, flushPromises, enableAutoUnmount } from '@vue/test-utils'
import { createMemoryHistory, createRouter } from 'vue-router'
import type { Proposal } from '../../../../types/automation'
import PaperReviewView from '../../../../views/paper/PaperReviewView.vue'
import { resetProposalDisplayNamesForTests } from '../../../../composables/useProposalDisplayNames'
import { i18n, DEFAULT_LOCALE } from '../../../../i18n'

/**
 * Review surface language switch (#1770 / ADR-0054 §8 rollout step 2).
 *
 * Mirrors `src/tests/views/AppearanceSettingsView.language.spec.ts`: the other
 * Review specs all assert literal English and stay on the default locale, which
 * proves the extraction did not change what an `en` user sees but proves nothing
 * about the `it`/`es` catalogs ever reaching the DOM. This file is the one that
 * flips the locale and looks.
 *
 * It also pins the two things the catalog guard cannot see:
 *   - plural selection (`review.decisionRail.summary.operations`) resolves per
 *     locale rather than rendering the raw pipe-separated source, and
 *   - a key absent from every catalog would render its own dotted path
 *     (`missingWarn: false`, ADR-0054 §5) — so no rendered string may look like
 *     a key path.
 */

const mocks = vi.hoisted(() => ({
  getProposals: vi.fn(),
  getProposal: vi.fn(),
  approveProposal: vi.fn(),
  rejectProposal: vi.fn(),
  deferProposal: vi.fn(),
  executeProposal: vi.fn(),
  getProposalDiff: vi.fn(),
  dismissProposals: vi.fn(),
  reportBadSuggestion: vi.fn(),
  getBoards: vi.fn(),
  getColumns: vi.fn(),
  getConfidence: vi.fn(),
  createRevision: vi.fn(),
  getRevisions: vi.fn(),
  getLatestRevision: vi.fn(),
  successToast: vi.fn(),
  errorToast: vi.fn(),
  infoToast: vi.fn(),
  sessionState: { userId: 'u-1' as string | null },
}))

vi.mock('../../../../api/automationApi', () => ({
  automationApi: {
    getProposals: mocks.getProposals,
    getProposal: mocks.getProposal,
    approveProposal: mocks.approveProposal,
    rejectProposal: mocks.rejectProposal,
    deferProposal: mocks.deferProposal,
    executeProposal: mocks.executeProposal,
    getProposalDiff: mocks.getProposalDiff,
    dismissProposals: mocks.dismissProposals,
    reportBadSuggestion: mocks.reportBadSuggestion,
  },
}))

vi.mock('../../../../api/boardsApi', () => ({
  boardsApi: { getBoards: mocks.getBoards },
}))

vi.mock('../../../../api/columnsApi', () => ({
  columnsApi: { getColumns: mocks.getColumns },
}))

// The view reads the collaboration-membership contract on mount (#1940). These
// specs are not about that contract, so it resolves to a collaborative
// workspace, which is the shape that leaves every queue filter on screen.
// PaperReviewMembershipFilter.spec.ts owns the membership behaviour itself.
vi.mock('../../../../api/workspaceApi', () => ({
  workspaceApi: {
    getCollaboration: vi.fn().mockResolvedValue({ memberCount: 2, hasCollaborators: true }),
  },
}))

vi.mock('../../../../api/proposalDeepReviewApi', () => ({
  proposalDeepReviewApi: {
    getProvenance: vi.fn().mockResolvedValue([]),
    // Hoisted so a single test can return a NON-EMPTY components array; the
    // default below keeps every other test on the empty-breakdown fixture.
    getConfidence: mocks.getConfidence,
    // Rejected so the CATALOG fallback copy is what renders, not a server string.
    getSideEffects: vi.fn().mockRejectedValue(new Error('unavailable')),
    getConflicts: vi.fn().mockResolvedValue([]),
    getHistory: vi.fn().mockResolvedValue([]),
    getSimilarPast: vi.fn().mockResolvedValue({ decisions: [], applyRate: 0 }),
  },
}))

vi.mock('../../../../store/toastStore', () => ({
  useToastStore: () => ({
    success: mocks.successToast,
    error: mocks.errorToast,
    info: mocks.infoToast,
  }),
}))

vi.mock('../../../../store/sessionStore', () => ({
  useSessionStore: () => mocks.sessionState,
}))

vi.mock('../../../../api/proposalRevisionsApi', () => ({
  proposalRevisionsApi: {
    createRevision: mocks.createRevision,
    getRevisions: mocks.getRevisions,
    getLatestRevision: mocks.getLatestRevision,
  },
}))

function makeProposal(overrides: Partial<Proposal> = {}): Proposal {
  const now = new Date().toISOString()
  return {
    id: 'proposal-001',
    sourceType: 'Chat',
    sourceReferenceId: null,
    boardId: 'board-1',
    requestedByUserId: 'u-1',
    status: 'PendingReview',
    riskLevel: 'Low',
    summary: 'Split dark mode into 3 cards',
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
    operations: [
      {
        id: 'op-1',
        proposalId: 'proposal-001',
        sequence: 0,
        actionType: 'CreateCard',
        targetType: 'Card',
        targetId: null,
        parameters: '{}',
        idempotencyKey: 'k-1',
        expectedVersion: null,
      },
      {
        id: 'op-2',
        proposalId: 'proposal-001',
        sequence: 1,
        actionType: 'CreateCard',
        targetType: 'Card',
        targetId: null,
        parameters: '{}',
        idempotencyKey: 'k-2',
        expectedVersion: null,
      },
    ],
    approvedRevisionId: null,
    latestRevisionId: null,
    ...overrides,
  }
}

async function mountView(proposals: Proposal[]) {
  mocks.getProposals.mockResolvedValue(proposals)
  mocks.getBoards.mockResolvedValue([])
  mocks.getColumns.mockResolvedValue([])
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/workspace/review', name: 'workspace-review', component: PaperReviewView }],
  })
  router.push('/workspace/review')
  await router.isReady()

  const wrapper = mount(PaperReviewView, { global: { plugins: [router] } })
  await flushPromises()
  return wrapper
}

describe('PaperReviewView — language', () => {
  enableAutoUnmount(afterEach)

  beforeEach(() => {
    vi.clearAllMocks()
    resetProposalDisplayNamesForTests()
    mocks.sessionState.userId = 'u-1'
    mocks.getRevisions.mockResolvedValue([])
    mocks.getLatestRevision.mockResolvedValue(null)
    mocks.getConfidence.mockResolvedValue({
      overall: 0.84,
      components: [],
      note: null,
      threshold: null,
      source: 'model-reported',
      meetsThreshold: null,
    })
    i18n.global.locale.value = DEFAULT_LOCALE
  })

  afterEach(() => {
    // The i18n instance is module-scoped and shared with every other spec file.
    i18n.global.locale.value = DEFAULT_LOCALE
    vi.restoreAllMocks()
  })

  it('renders the surface in English on the default locale', async () => {
    const wrapper = await mountView([makeProposal()])

    expect(wrapper.find('[data-testid="paper-review-queue-rail"]').text()).toContain('awaiting')
    // The visible face of the primary button; the other phase's label is also
    // in the DOM as the width reservation (GH-1942), so assert the face.
    expect(wrapper.find('[data-testid="decision-apply-label"]').text()).toBe('Approve')
    expect(wrapper.find('[data-testid="paper-review-key-hint"]').text()).toBe(
      'PRESS ⏎ TO APPROVE · ⌫ TO REJECT',
    )
  })

  it('re-renders every column in Italian when the locale switches', async () => {
    const wrapper = await mountView([makeProposal()])
    i18n.global.locale.value = 'it'
    await flushPromises()

    // Left rail (own component), centre column (orchestrator computed passed as
    // a prop), and right rail (static card) each prove a different code path.
    expect(wrapper.find('[data-testid="paper-review-queue-rail"]').text()).toContain('in attesa')
    expect(wrapper.find('[data-testid="paper-review-main"]').text()).toContain(
      'revisione esplicita',
    )
    expect(wrapper.find('[data-testid="paper-review-right-rail"]').text()).toContain(
      'Decidi con i tasti',
    )
    expect(wrapper.get('[data-testid="paper-review-confidence-disclosure"]').text()).toContain(
      'Mostra i dettagli della confidenza',
    )
    expect(wrapper.get('[data-testid="paper-review-provenance-disclosure"]').text()).toContain(
      'Mostra i dettagli della provenienza',
    )
    expect(wrapper.get('[data-testid="paper-review-similar-past-disclosure"]').text()).toContain(
      'Mostra decisioni simili',
    )
    expect(wrapper.find('[data-testid="paper-review-key-hint"]').text()).toBe(
      'PREMI ⏎ PER APPROVARE · ⌫ PER RIFIUTARE',
    )
    expect(wrapper.find('[data-testid="decision-apply-label"]').text()).toBe('Approva')
    // The reserved (hidden) face is translated too — otherwise the button would
    // reserve the width of an English label it can never show.
    expect(wrapper.find('[data-testid="decision-apply-reserve"]').text()).toBe(
      'Applica alla bacheca',
    )
    // No English left on the decision rail.
    expect(wrapper.find('[data-testid="decision-step-hint"]').text()).not.toContain('Step 1 of 2')
  })

  it('selects the right plural form per locale, never the raw pipe source', async () => {
    const wrapper = await mountView([makeProposal()])
    const summary = () => wrapper.find('.paper-review-decision__summary').text()

    // Two operations on the fixture → the plural branch in all three locales.
    expect(summary()).toBe('2 operations · explicit review · atomic apply')
    expect(summary()).not.toContain('|')

    i18n.global.locale.value = 'it'
    await flushPromises()
    expect(summary()).toBe('2 operazioni · revisione esplicita · applicazione atomica')
    expect(summary()).not.toContain('|')

    i18n.global.locale.value = 'es'
    await flushPromises()
    expect(summary()).toBe('2 operaciones · revisión explícita · aplicación atómica')
    expect(summary()).not.toContain('|')
  })

  it('translates the fallback copy the composables own, not just template copy', async () => {
    // `getSideEffects` is rejected above, so the apply-risk card shows the
    // catalog fallback built inside `usePaperReviewSelectors` — the composable
    // path that resolves keys through the module-scoped i18n runtime.
    const wrapper = await mountView([makeProposal()])
    expect(wrapper.find('[data-testid="apply-risk-posture"]').text()).toContain(
      'Risk details unavailable',
    )

    i18n.global.locale.value = 'es'
    await flushPromises()
    expect(wrapper.find('[data-testid="apply-risk-posture"]').text()).toContain(
      'Detalles del riesgo no disponibles',
    )
  })

  it('keeps exact model-reported operation labels stable across locale switches', async () => {
    mocks.getConfidence.mockResolvedValue({
      overall: 0.84,
      components: [
        { key: 'Operation 1: create card', value: 0.92 },
        { key: 'Operation 2: update card', value: 0.4 },
      ],
      note: null,
      threshold: null,
      source: 'model-reported',
      meetsThreshold: null,
    })

    const wrapper = await mountView([makeProposal()])
    const barKeys = () =>
      wrapper.findAll('.paper-review-author__bar-key').map((n) => n.text())

    expect(barKeys()).toEqual(['Operation 1: create card', 'Operation 2: update card'])

    i18n.global.locale.value = 'it'
    await flushPromises()
    expect(barKeys()).toEqual(['Operation 1: create card', 'Operation 2: update card'])

    i18n.global.locale.value = 'es'
    await flushPromises()
    expect(barKeys()).toEqual(['Operation 1: create card', 'Operation 2: update card'])
  })

  it('never leaks a raw key path into the rendered surface', async () => {
    const wrapper = await mountView([makeProposal()])
    for (const locale of ['en', 'it', 'es'] as const) {
      i18n.global.locale.value = locale
      await flushPromises()
      // A key missing from all three catalogs renders as its own dotted path
      // with no console warning (ADR-0054 §5); this is the only cheap net.
      expect(wrapper.text(), `raw key path rendered in "${locale}"`).not.toMatch(/\breview\.\w+\./)
    }
  })
})
