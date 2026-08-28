import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ReviewQueueRail, {
  type QueueRailItem,
} from '../../../../views/paper/review/ReviewQueueRail.vue'
import type { RecentlyAppliedRow } from '../../../../views/paper/review/ReviewRecentApplied.vue'

function makeItem(overrides: Partial<QueueRailItem> = {}): QueueRailItem {
  return {
    id: 'p-1',
    serial: '#0001',
    title: 'A proposal',
    who: 'assistant',
    confidence: 0.84,
    age: '4s',
    reach: '3 cards · 1 board',
    mine: false,
    stale: false,
    ...overrides,
  }
}

function mountRail(props?: Partial<{
  items: QueueRailItem[]
  activeId: string | null
  recentlyApplied: RecentlyAppliedRow[]
  dismissableCount: number
  busy: boolean
  batchSelectedCount: number
  applyRate: number
  cadence: number[]
  scopeLabel: string
  scopeClearLabel: string
}>) {
  return mount(ReviewQueueRail, {
    props: {
      items: props?.items ?? [makeItem()],
      activeId: props?.activeId ?? null,
      awaitingCount: 3,
      staleCount: 2,
      dismissableCount: props?.dismissableCount ?? 0,
      busy: props?.busy ?? false,
      batchSelectedCount: props?.batchSelectedCount ?? 0,
      recentlyApplied: props?.recentlyApplied ?? [],
      ...(props?.applyRate !== undefined ? { applyRate: props.applyRate } : {}),
      ...(props?.cadence !== undefined ? { cadence: props.cadence } : {}),
      ...(props?.scopeLabel ? { scopeLabel: props.scopeLabel, scopeClearLabel: props.scopeClearLabel } : {}),
    },
  })
}

describe('ReviewQueueRail', () => {
  it('renders one row per item with the correct serial and title', () => {
    const items = [
      makeItem({ id: 'a', serial: '#A001', title: 'First' }),
      makeItem({ id: 'b', serial: '#A002', title: 'Second', stale: true }),
    ]
    const wrapper = mountRail({ items })
    const rows = wrapper.findAll('.paper-review-q')
    expect(rows).toHaveLength(2)
    expect(rows[0].text()).toContain('#A001')
    expect(rows[0].text()).toContain('First')
    expect(rows[1].text()).toContain('Second')
  })

  it('marks the active row with the ember class', () => {
    const items = [
      makeItem({ id: 'a' }),
      makeItem({ id: 'b' }),
    ]
    const wrapper = mountRail({ items, activeId: 'b' })
    const rows = wrapper.findAll('.paper-review-q')
    expect(rows[0].classes()).not.toContain('paper-review-q--active')
    expect(rows[1].classes()).toContain('paper-review-q--active')
  })

  it('marks stale rows so the parent CSS can dim them', () => {
    const items = [makeItem({ stale: true })]
    const wrapper = mountRail({ items })
    expect(wrapper.find('.paper-review-q').classes()).toContain('paper-review-q--stale')
  })

  it('emits select when a row is clicked', async () => {
    const wrapper = mountRail({ items: [makeItem({ id: 'p-42' })] })
    await wrapper.find('.paper-review-q').trigger('click')
    expect(wrapper.emitted('select')).toBeDefined()
    expect(wrapper.emitted('select')?.[0]).toEqual(['p-42'])
  })

  it('renders queue rows as native buttons for keyboard activation', () => {
    const wrapper = mountRail({ items: [makeItem({ id: 'p-42' })] })
    const row = wrapper.find('.paper-review-q')
    expect(row.element.tagName).toBe('BUTTON')
    expect(row.attributes('type')).toBe('button')
  })

  it('filter pill "Mine" hides items not flagged as mine', async () => {
    const items = [
      makeItem({ id: 'a', mine: true, title: 'Mine 1' }),
      makeItem({ id: 'b', mine: false, title: 'Theirs 1' }),
    ]
    const wrapper = mountRail({ items })
    expect(wrapper.findAll('.paper-review-q')).toHaveLength(2)

    const pills = wrapper.findAll('.paper-review-rail__pill')
    const minePill = pills.find((p) => p.text() === 'Mine')!
    await minePill.trigger('click')

    const visible = wrapper.findAll('.paper-review-q')
    expect(visible).toHaveLength(1)
    expect(visible[0].text()).toContain('Mine 1')
    expect(minePill.attributes('aria-pressed')).toBe('true')
    expect(wrapper.emitted('filter-change')?.[0]).toEqual(['mine'])
  })

  it('filter pill "Stale" only shows stale items', async () => {
    const items = [
      makeItem({ id: 'a', stale: false, title: 'Fresh' }),
      makeItem({ id: 'b', stale: true, title: 'Stale 1' }),
    ]
    const wrapper = mountRail({ items })
    const stalePill = wrapper
      .findAll('.paper-review-rail__pill')
      .find((p) => p.text() === 'Stale')!
    await stalePill.trigger('click')
    const visible = wrapper.findAll('.paper-review-q')
    expect(visible).toHaveLength(1)
    expect(visible[0].text()).toContain('Stale 1')
  })

  it('renders the awaiting/stale counts in the eyebrow', () => {
    const wrapper = mountRail()
    expect(wrapper.text()).toContain('3 awaiting')
    expect(wrapper.text()).toContain('2 stale')
  })

  it('makes the board scope visible alongside the assignment filters and emits its clear action', async () => {
    const wrapper = mountRail({
      scopeLabel: 'Board: Payments API Migration',
      scopeClearLabel: 'Show all boards',
    })

    expect(wrapper.find('[data-testid="paper-scope-disclosure"]').text()).toContain('Board: Payments API Migration')
    expect(wrapper.find('[data-testid="paper-scope-disclosure"]').text()).toContain('Show all boards')
    expect(wrapper.find('.paper-review-rail__pill--active').text()).toBe('All')

    await wrapper.find('[data-testid="paper-scope-clear"]').trigger('click')
    expect(wrapper.emitted('clear-scope')).toHaveLength(1)
  })

  it('hides the bulk file-away action only when there are no settled proposals', () => {
    expect(mountRail({ dismissableCount: 0 }).find('[data-testid="queue-file-away-all"]').exists()).toBe(false)
  })

  it('shows the bulk file-away action for a single settled proposal', () => {
    const bulk = mountRail({ dismissableCount: 1 }).find('[data-testid="queue-file-away-all"]')
    expect(bulk.exists()).toBe(true)
    expect(bulk.text()).toContain('File away 1 settled')
  })

  it('shows the bulk file-away action with the count and emits file-away-all on click', async () => {
    const wrapper = mountRail({ dismissableCount: 3 })
    const bulk = wrapper.find('[data-testid="queue-file-away-all"]')
    expect(bulk.exists()).toBe(true)
    expect(bulk.text()).toContain('File away 3 settled')
    expect(bulk.attributes('aria-label')).toBe('File away 3 settled proposals')

    await bulk.trigger('click')
    expect(wrapper.emitted('file-away-all')).toHaveLength(1)
  })

  it('disables the bulk file-away action while a review action is in flight', () => {
    const wrapper = mountRail({ dismissableCount: 3, busy: true })
    expect(wrapper.find('[data-testid="queue-file-away-all"]').attributes('disabled')).toBeDefined()
  })

  it('renders selection only for eligible rows as a sibling of the row button', () => {
    const wrapper = mountRail({
      items: [
        makeItem({ id: 'eligible', title: 'Eligible card', batchEligible: true }),
        makeItem({ id: 'ineligible', title: 'Needs individual review', batchEligible: false }),
      ],
    })

    const checkbox = wrapper.find('[data-testid="queue-batch-select-eligible"]')
    expect(checkbox.exists()).toBe(true)
    expect(checkbox.element.tagName).toBe('INPUT')
    expect(checkbox.attributes('type')).toBe('checkbox')
    expect(checkbox.attributes('aria-label')).toBe('Select Eligible card for batch approval')
    expect(wrapper.find('[data-testid="queue-batch-select-ineligible"]').exists()).toBe(false)
    expect(checkbox.element.parentElement?.contains(wrapper.find('.paper-review-q').element)).toBe(false)
  })

  it('emits independent selection and explicit confirmation requests', async () => {
    const wrapper = mountRail({
      items: [makeItem({ id: 'p-42', batchEligible: true, batchSelected: true })],
      batchSelectedCount: 1,
    })

    const checkbox = wrapper.find('[data-testid="queue-batch-select-p-42"]')
    expect((checkbox.element as HTMLInputElement).checked).toBe(true)
    await checkbox.trigger('change')
    expect(wrapper.emitted('toggle-batch')?.[0]).toEqual(['p-42'])
    expect(wrapper.emitted('select')).toBeUndefined()

    const confirm = wrapper.find('[data-testid="queue-batch-approve"]')
    expect(confirm.text()).toContain('Review 1 selected approval')
    await confirm.trigger('click')
    expect(wrapper.emitted('request-batch-approval')).toHaveLength(1)
  })

  it('disables batch selection and confirmation under the shared review lock', () => {
    const wrapper = mountRail({
      items: [makeItem({ id: 'p-42', batchEligible: true })],
      batchSelectedCount: 1,
      busy: true,
    })

    expect(wrapper.find('[data-testid="queue-batch-select-p-42"]').attributes('disabled')).toBeDefined()
    expect(wrapper.find('[data-testid="queue-batch-approve"]').attributes('disabled')).toBeDefined()
  })

  it('renders recently-applied rows as native exact-id controls', async () => {
    const wrapper = mountRail({
      recentlyApplied: [
        { id: 'recent-1', serial: '#R01', title: 'Move done', age: '12m' },
        { id: 'recent-2', serial: '#R02', title: 'Old apply', age: '5h' },
      ],
    })
    expect(wrapper.text()).toContain('12m ago')
    expect(wrapper.text()).toContain('5h ago')
    expect(wrapper.text()).not.toContain('undo')
    expect(wrapper.text()).not.toContain('sealed')

    const rows = wrapper.findAll('.paper-review-recent__row')
    expect(rows).toHaveLength(2)
    expect(rows[0].element.tagName).toBe('BUTTON')
    expect(rows[0].attributes('type')).toBe('button')
    expect(rows[0].attributes('aria-label')).toBe('Open applied proposal: Move done')
    expect(rows[1].attributes('data-proposal-id')).toBe('recent-2')

    await rows[1].trigger('click')
    expect(wrapper.emitted('select')?.at(-1)).toEqual(['recent-2'])
  })

  describe('This week apply-rate stat', () => {
    it('shows the empty state and never a fabricated percentage when no apply rate is provided', () => {
      // A fresh account with zero decision history: the rail must not invent a
      // statistic. This is the mutation guard for the old `applyRate: 0.71`
      // default — restoring it would render "71%" and hide the empty state,
      // failing both assertions below.
      const wrapper = mountRail()
      const empty = wrapper.find('[data-testid="paper-review-apply-rate-empty"]')
      expect(empty.exists()).toBe(true)
      expect(empty.text()).toBe('No decisions yet')
      expect(wrapper.find('[data-testid="paper-review-apply-rate"]').exists()).toBe(false)
      expect(wrapper.text()).not.toContain('Apply rate')
      expect(wrapper.text()).not.toContain('71%')
      expect(wrapper.text()).not.toMatch(/\d+%/)
    })

    it('renders the real apply rate as a rounded percentage when provided', () => {
      const wrapper = mountRail({ applyRate: 0.5 })
      const stat = wrapper.find('[data-testid="paper-review-apply-rate"]')
      expect(stat.exists()).toBe(true)
      expect(stat.text()).toContain('Apply rate')
      expect(stat.text()).toContain('50%')
      expect(wrapper.find('[data-testid="paper-review-apply-rate-empty"]').exists()).toBe(false)
    })

    it('renders a real apply rate of zero as 0%, not the empty state', () => {
      // 0 is a real decision-history value (nothing applied yet), distinct from
      // "no history at all" — it must render honestly, not fall through to the
      // empty state or a fabricated default.
      const wrapper = mountRail({ applyRate: 0 })
      const stat = wrapper.find('[data-testid="paper-review-apply-rate"]')
      expect(stat.exists()).toBe(true)
      expect(stat.text()).toContain('0%')
      expect(wrapper.find('[data-testid="paper-review-apply-rate-empty"]').exists()).toBe(false)
    })

    it('hides the mini-cadence bars when no real cadence data is provided', () => {
      const wrapper = mountRail()
      expect(wrapper.find('.paper-review-cadence').exists()).toBe(false)
    })

    it('renders the mini-cadence bars from real cadence data when provided', () => {
      const wrapper = mountRail({ cadence: [1, 2, 3, 4, 5, 6, 7] })
      const bars = wrapper.findAll('.paper-review-cadence__bar')
      expect(bars).toHaveLength(7)
    })
  })
})
