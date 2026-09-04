import { describe, expect, it } from 'vitest'
import { h, nextTick, ref } from 'vue'
import { mount } from '@vue/test-utils'
import ReviewMain from '../../../../views/paper/review/ReviewMain.vue'
import type {
  ChangeAfterCard,
  ChangeBeforeCard,
  FieldDiff,
} from '../../../../views/paper/review/ReviewChangeSection.vue'
import type {
  ConfidenceBreakdown,
  ConflictRow,
  HistoryRow,
  ProvenanceRow,
  SideEffects,
} from '../../../../composables/usePaperReviewSelectors'

const before: ChangeBeforeCard = {
  serial: 'C-1',
  title: 'Before title',
  body: 'Before body.',
  meta: '· labels · 0/0 subtasks',
}

const after: ChangeAfterCard[] = [
  { serial: 'C-1', title: 'A', body: 'a', status: 'kept' },
  { serial: 'C-1a', title: 'B', body: 'b', status: 'new' },
]

const fields: FieldDiff[] = [
  { key: 'title', before: 'Old', after: 'New' },
  { key: 'assignee', before: 'X', after: 'X', same: true },
]

const provenance: ProvenanceRow[] = [
  { icon: '📄', key: 'card body', value: 'desc', weight: 'primary' },
]

const sideEffects: SideEffects = {
  rows: [{ key: 'Cards', value: '1 created', tone: 'active' }],
  applyRisk: {
    summary: 'Low risk · confirm before apply',
    description: 'Confirm affected items.',
  },
}

const conflicts: ConflictRow[] = []
const history: HistoryRow[] = []

type DeepReviewOptions = {
  conflicts?: ConflictRow[]
  history?: HistoryRow[]
  applyPhase?: 'approve' | 'execute'
  dismissable?: boolean
  attachTo?: boolean
  busy?: boolean
  attrs?: Record<string, string>
}

function mainProps(
  confidence: Partial<ConfidenceBreakdown> = {},
  deepReview: DeepReviewOptions = {},
) {
  return {
    serial: '#2026-04-25-014',
    meta: '11:42 PT · awaiting decision',
    titleParts: [
      { text: 'Split ' },
      { text: '“dark mode”', emphasis: true },
      { text: ' into 3 cards' },
    ],
    lede: 'Lede text.',
    decisionSummary: '3 ops · explicit review · atomic apply',
    busy: deepReview.busy ?? false,
    confidence: {
      overall: confidence.overall === undefined ? 0.84 : confidence.overall,
      components: confidence.components ?? [],
      threshold: null,
      note: confidence.note,
      source: confidence.source ?? 'model-reported',
    },
    before,
    after,
    fields,
    changeSubTitle: '3 changes',
    provenance,
    proposalId: 'proposal-001',
    sideEffects,
    conflicts: deepReview.conflicts ?? conflicts,
    history: deepReview.history ?? history,
    applyPhase: deepReview.applyPhase ?? 'approve',
    dismissable: deepReview.dismissable ?? false,
  }
}

function mountMain(
  confidence: Partial<ConfidenceBreakdown> = {},
  deepReview: DeepReviewOptions = {},
) {
  return mount(ReviewMain, {
    attachTo: deepReview.attachTo ? document.body : undefined,
    attrs: deepReview.attrs,
    props: mainProps(confidence, deepReview),
  })
}

describe('ReviewMain', () => {
  it('passes confidence value to the dial (rounded to 2 decimals)', () => {
    const wrapper = mountMain({ overall: 0.84 })
    const dial = wrapper.find('[data-testid="paper-review-confidence-dial"]')
    expect(dial.exists()).toBe(true)
    // The dial text node renders 0.84 as ".84" (leading zero stripped).
    expect(dial.text()).toContain('.84')
  })

  it('renders the title with emphasized fragments wrapped in <em>', () => {
    const wrapper = mountMain()
    const h1 = wrapper.find('h1.paper-review-main__title')
    expect(h1.exists()).toBe(true)
    const em = h1.find('em')
    expect(em.exists()).toBe(true)
    expect(em.text()).toBe('“dark mode”')
  })

  it('emits decision events when each rail button is clicked', async () => {
    const wrapper = mountMain()
    await wrapper.get('[data-testid="decision-apply"]').trigger('click')
    await wrapper.get('[data-testid="decision-reject"]').trigger('click')
    await wrapper.get('[data-testid="decision-edit"]').trigger('click')
    await wrapper.get('[data-testid="decision-defer"]').trigger('click')

    expect(wrapper.emitted('apply')).toHaveLength(1)
    expect(wrapper.emitted('reject')).toHaveLength(1)
    expect(wrapper.emitted('request-edit')).toHaveLength(1)
    expect(wrapper.emitted('defer')).toHaveLength(1)
  })

  it('forwards provenance report events with the active proposal id', async () => {
    const wrapper = mountMain()

    await wrapper.get('[data-testid="paper-review-provenance-disclosure"]').trigger('click')
    await wrapper.get('.paper-review-prov__more').trigger('click')
    await wrapper.vm.$nextTick()
    const reportButton = document.body.querySelector('.prov-drawer__action--report') as HTMLButtonElement
    await reportButton.click()

    expect(wrapper.emitted('report')).toEqual([['proposal-001']])
  })

  it('labels the numeric dial as model-reported without apply-threshold language', () => {
    const wrapper = mountMain({ overall: 0.9, source: 'model-reported' })
    expect(wrapper.text()).toContain('Reported item average')
    expect(wrapper.text()).not.toContain('apply threshold')
  })

  it('shows deterministic provenance without inventing a confidence number', () => {
    const wrapper = mountMain({ overall: null, source: 'deterministic' })

    expect(wrapper.find('[data-testid="paper-review-confidence-dial"]').exists()).toBe(false)
    expect(wrapper.get('[data-testid="paper-review-confidence-source"]').text()).toContain(
      'DETERMINISTIC',
    )
    expect(wrapper.text()).toContain('No model confidence number')
  })

  // --- #1818: approved-but-not-executed must read differently from pending ---

  describe('two-phase apply feedback', () => {
    it('shows no approved banner while the proposal is still pending', () => {
      const wrapper = mountMain()
      expect(wrapper.find('[data-testid="paper-review-approved-banner"]').exists()).toBe(false)
      expect(wrapper.get('[data-testid="paper-review-key-hint"]').text()).toBe(
        'PRESS ⏎ TO APPROVE · ⌫ TO REJECT',
      )
    })

    it('states "Approved — not yet applied to the board" once approved', () => {
      const wrapper = mountMain({}, { applyPhase: 'execute' })
      const banner = wrapper.get('[data-testid="paper-review-approved-banner"]')
      expect(banner.text()).toContain('Approved — not yet applied to the board.')
      // The banner must name the NEXT action, not just the state — and after
      // GH-1942 there is exactly ONE action left, so it must not warn about a
      // further confirmation step that no longer exists.
      expect(banner.text()).toContain('Apply to board')
      expect(banner.text()).toContain('One step left')
      expect(banner.text()).not.toContain('you will be asked to confirm')
      expect(banner.attributes('role')).toBe('status')
    })

    it('makes the keyboard hint name the phase ⏎ will actually run', () => {
      const wrapper = mountMain({}, { applyPhase: 'execute' })
      expect(wrapper.get('[data-testid="paper-review-key-hint"]').text()).toBe(
        'PRESS ⏎ TO APPLY TO BOARD',
      )
    })

    it('shows the filing hint and no approved banner once the proposal is settled', () => {
      // Applied (or otherwise terminal): distinct from BOTH pending and approved.
      const wrapper = mountMain({}, { dismissable: true, applyPhase: 'execute' })
      expect(wrapper.find('[data-testid="paper-review-approved-banner"]').exists()).toBe(false)
      expect(wrapper.get('[data-testid="paper-review-key-hint"]').text()).toBe(
        'PRESS ⌫ TO FILE AWAY',
      )
      expect(wrapper.text()).toContain('SETTLED')
    })
  })

  describe('decision receipt focus', () => {
    it.each(['applied', 'rejected', 'deferred'] as const)(
      'moves focus to the %s receipt at its original decision locus',
      async (decisionReceipt) => {
        const wrapper = mountMain({}, { attachTo: true })
        document.body.focus()
        expect(document.activeElement).toBe(document.body)

        await wrapper.setProps({ decisionReceipt })
        await wrapper.vm.$nextTick()

        expect(document.activeElement).toBe(
          wrapper.get('[data-testid="paper-review-decision-receipt"]').element,
        )

        wrapper.unmount()
      },
    )

    it('moves focus to the remaining explicit Apply control after approval', async () => {
      const wrapper = mountMain({}, { applyPhase: 'execute', attachTo: true })
      document.body.focus()
      expect(document.activeElement).toBe(document.body)

      await wrapper.setProps({ decisionReceipt: 'approved' })
      await wrapper.vm.$nextTick()

      expect(document.activeElement).toBe(wrapper.get('[data-testid="decision-apply"]').element)

      wrapper.unmount()
    })
  })

  /**
   * #2461 — the Review view renders the post-revision refresh lock above this
   * column and describes the column with its id. That attribute lands on this
   * wrapper, which is not focusable, so assistive tech inspecting a disabled
   * decision button never reached the explanation.
   */
  describe('decision lock description (#2461)', () => {
    const LOCK_ID = 'paper-review-revision-refresh-lock'
    const decisionTestIds = [
      'decision-reject',
      'decision-edit',
      'decision-defer',
      'decision-apply',
    ]

    it('forwards the column description ids to every disabled decision control', () => {
      const note = document.createElement('p')
      note.id = LOCK_ID
      note.textContent = 'Refreshing this proposal before your decision.'
      document.body.appendChild(note)

      const wrapper = mountMain(
        {},
        { attachTo: true, busy: true, attrs: { 'aria-describedby': LOCK_ID } },
      )

      // The wrapper keeps the attribute: those notes describe the whole column,
      // and the Review view asserts that association.
      expect(wrapper.get('[data-testid="paper-review-main"]').attributes('aria-describedby'))
        .toBe(LOCK_ID)

      for (const testid of decisionTestIds) {
        const button = wrapper.get(`[data-testid="${testid}"]`)
        expect(button.attributes('disabled')).toBeDefined()
        const ids = (button.attributes('aria-describedby') ?? '').split(' ').filter(Boolean)
        expect(ids).toContain(LOCK_ID)
        // Attached to the real document, so every referenced id must resolve.
        for (const id of ids) {
          expect(document.getElementById(id)).not.toBeNull()
        }
      }

      wrapper.unmount()
      note.remove()
    })

    it('leaves the decision controls undescribed when the column has no explanation', () => {
      const wrapper = mountMain({}, { attachTo: true, busy: true })

      expect(wrapper.get('[data-testid="paper-review-main"]').attributes('aria-describedby'))
        .toBeUndefined()
      for (const testid of decisionTestIds) {
        expect(wrapper.get(`[data-testid="${testid}"]`).attributes('aria-describedby'))
          .toBeUndefined()
      }

      wrapper.unmount()
    })

    it('drops the association from the controls when the lock clears', async () => {
      // Fallthrough attributes are not reactive, so a cached read would keep
      // pointing at a note the Review view has already removed. Driven from a
      // host that re-renders, exactly as the Review view does.
      const note = document.createElement('p')
      note.id = LOCK_ID
      note.textContent = 'Refreshing this proposal before your decision.'
      document.body.appendChild(note)

      const describedBy = ref<string | undefined>(LOCK_ID)
      const host = mount(
        {
          render: () =>
            h(ReviewMain, {
              ...mainProps({}, { busy: true }),
              'aria-describedby': describedBy.value,
            }),
        },
        { attachTo: document.body },
      )

      expect(host.get('[data-testid="decision-apply"]').attributes('aria-describedby'))
        .toBe(LOCK_ID)

      note.remove()
      describedBy.value = undefined
      await nextTick()

      expect(host.get('[data-testid="paper-review-main"]').attributes('aria-describedby'))
        .toBeUndefined()
      expect(host.get('[data-testid="decision-apply"]').attributes('aria-describedby'))
        .toBeUndefined()

      host.unmount()
    })
  })

  it('renders malformed enum fallbacks as user-visible attention states', () => {
    const wrapper = mountMain({}, {
      conflicts: [{ tone: 'warn', key: 'Unknown conflict', value: 'Review required' }],
      history: [{ serial: '#1', event: 'Unknown event', age: 'now', status: 'unknown' }],
    })

    expect(wrapper.text()).toContain('What the system noticed · 1 minor')
    expect(wrapper.text()).toContain('WARNING')
    expect(wrapper.get('[data-status="unknown"]').text()).toContain('UNKNOWN')
  })
})
