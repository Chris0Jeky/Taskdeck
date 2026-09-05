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
  batchExecutableCount: number
  cadence: number[]
  scopeLabel: string
  scopeClearLabel: string
  queueScopeLoaded: boolean
  queueUnavailable: boolean
  awaitingCount: number
  announcementKey: string
  attachTo: boolean
  /**
   * The RETIRED pre-fix input (#2599 item 1). It is no longer a prop, so the
   * rail receives it as an inert fallthrough attribute; only the regression pin
   * below passes it, and only so that test fails on the pre-fix rail, where
   * this was the announcement gate's first term.
   */
  loading: boolean
}>) {
  return mount(ReviewQueueRail, {
    ...(props?.attachTo ? { attachTo: document.body } : {}),
    props: {
      items: props?.items ?? [makeItem()],
      activeId: props?.activeId ?? null,
      awaitingCount: props?.awaitingCount ?? 3,
      staleCount: 2,
      ...(props?.announcementKey !== undefined ? { announcementKey: props.announcementKey } : {}),
      ...(props?.queueScopeLoaded !== undefined ? { queueScopeLoaded: props.queueScopeLoaded } : {}),
      ...(props?.loading !== undefined ? { loading: props.loading } : {}),
      ...(props?.queueUnavailable !== undefined ? { queueUnavailable: props.queueUnavailable } : {}),
      dismissableCount: props?.dismissableCount ?? 0,
      busy: props?.busy ?? false,
      batchSelectedCount: props?.batchSelectedCount ?? 0,
      batchExecutableCount: props?.batchExecutableCount ?? 0,
      recentlyApplied: props?.recentlyApplied ?? [],
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

  describe('This week cadence', () => {
    it('renders real cadence without an apply-rate metric or empty claim', () => {
      const wrapper = mountRail({ cadence: [1, 2, 3, 4, 5, 6, 7] })

      expect(wrapper.findAll('.paper-review-cadence__bar')).toHaveLength(7)
      expect(wrapper.find('[data-testid="paper-review-apply-rate"]').exists()).toBe(false)
      expect(wrapper.find('[data-testid="paper-review-apply-rate-empty"]').exists()).toBe(false)
      expect(wrapper.text()).not.toContain('Apply rate')
      expect(wrapper.text()).not.toContain('No decisions yet')
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

describe('ReviewQueueRail apply-approved action (#1307)', () => {
  it('hides the apply-approved action when nothing is approved', () => {
    const wrapper = mountRail({ batchExecutableCount: 0 })
    expect(wrapper.find('[data-testid="queue-batch-execute"]').exists()).toBe(false)
  })

  it('offers the apply-approved action with its count and asks the parent to confirm', async () => {
    const wrapper = mountRail({ batchExecutableCount: 3 })
    const apply = wrapper.find('[data-testid="queue-batch-execute"]')

    expect(apply.exists()).toBe(true)
    expect(apply.text()).toContain('3')
    // The rail never applies anything itself: it asks the view to open the confirmation.
    await apply.trigger('click')
    expect(wrapper.emitted('request-batch-execute')).toHaveLength(1)
  })

  it('is disabled while a review action is in flight', () => {
    const wrapper = mountRail({ batchExecutableCount: 2, busy: true })
    expect(wrapper.find('[data-testid="queue-batch-execute"]').attributes('disabled')).toBeDefined()
  })

  it('stands beside batch approve without replacing it', () => {
    const wrapper = mountRail({ batchExecutableCount: 2, batchSelectedCount: 1 })
    expect(wrapper.find('[data-testid="queue-batch-approve"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="queue-batch-execute"]').exists()).toBe(true)
  })
})

describe('ReviewQueueRail queue announcement (#2214)', () => {
  it('announces the awaiting count once a read has landed for the current scope', () => {
    const wrapper = mountRail({ awaitingCount: 3, queueScopeLoaded: true })
    const live = wrapper.get('[data-testid="paper-review-queue-live"]')
    expect(live.attributes('role')).toBe('status')
    expect(live.text()).toContain('3 proposals awaiting review')
  })

  it('announces nothing before a read has landed for the current scope', () => {
    // Before the first read the rail carries awaitingCount 0 because nothing
    // has been read yet, so an ungated region reads "0 proposals awaiting
    // review." and then the real count. Only the content is withheld: the
    // region stays mounted so a later change lands in a live region that was
    // already present.
    const wrapper = mountRail({ queueScopeLoaded: false, awaitingCount: 0 })
    const live = wrapper.get('[data-testid="paper-review-queue-live"]')
    expect(live.attributes('role')).toBe('status')
    expect(live.text()).toBe('')
  })

  it('announces nothing once queue access is revoked', () => {
    // The revoked state clears the queue, so awaitingCount drops to 0 for a
    // reason that is not "nothing is awaiting review". Its own gate, not the
    // scope signal, is what must withhold it -- hence the landed read here.
    const wrapper = mountRail({
      queueScopeLoaded: true,
      queueUnavailable: true,
      awaitingCount: 0,
      items: [],
    })
    const live = wrapper.get('[data-testid="paper-review-queue-live"]')
    expect(live.attributes('role')).toBe('status')
    expect(live.text()).toBe('')
  })

  it('withholds the announcement when no scope flag is supplied', () => {
    // The prop is optional and defaults to withholding: a parent that does not
    // say a read has landed cannot have its count spoken, because 0 from a
    // never-read queue is the #2593 defect (#2599 item 1).
    const wrapper = mountRail({ awaitingCount: 2 })
    expect(wrapper.get('[data-testid="paper-review-queue-live"]').text()).toBe('')
    expect(wrapper.find('[data-testid="paper-review-queue-announcement"]').exists()).toBe(false)
  })

  it('keeps the announcement node across a reload of the same scope (#2599 item 1)', async () => {
    // The rail's old gate was the parent's `loading` flag, so an explicit
    // reload -- which raises it without clearing the queue -- unmounted this
    // node and remounted it with the same sentence. A live region speaks a node
    // addition, so the reviewer heard the same count read back for a queue that
    // had not moved.
    //
    // A reload is invisible to the rail now, which is the fix and also why this
    // case has nothing of its own to drive: re-setting identical props does not
    // even re-render. So the pin is the retired input itself -- `loading: true`
    // is what the parent sent mid-reload before, and it withheld the
    // announcement. Here it is an inert fallthrough attribute and only the
    // scope signal decides, which is why this test is RED on the pre-fix rail
    // and green here. The end-to-end evidence that a real reload stays silent
    // is at view level: ReviewView.spec's and PaperReviewView.spec's
    // "keeps the same announcement node across an explicit reload" cases.
    const wrapper = mountRail({
      awaitingCount: 2,
      queueScopeLoaded: true,
      announcementKey: 'p-a\np-b',
      loading: true,
    })
    const announced = wrapper.get('[data-testid="paper-review-queue-announcement"]')
    expect(announced.text()).toContain('2 proposals awaiting review')
    const before = announced.element

    // A genuine re-render for an unrelated reason must not rebuild the keyed
    // node either: the identity key is what re-announces, never the render.
    await wrapper.setProps({ staleCount: 3, busy: true })

    expect(wrapper.get('[data-testid="paper-review-queue-announcement"]').element).toBe(before)
  })

  it('withholds the announcement when the scope changes under it, without remounting the region', async () => {
    // A board-filter change is the one reload where the rendered count really
    // does stop being a count of what is on screen, so it is withheld until the
    // new scope's read lands.
    const wrapper = mountRail({
      awaitingCount: 2,
      queueScopeLoaded: true,
      announcementKey: 'p-a\np-b',
    })
    const region = wrapper.get('[data-testid="paper-review-queue-live"]').element

    await wrapper.setProps({ queueScopeLoaded: false })
    expect(wrapper.find('[data-testid="paper-review-queue-announcement"]').exists()).toBe(false)
    expect(wrapper.get('[data-testid="paper-review-queue-live"]').text()).toBe('')

    await wrapper.setProps({ queueScopeLoaded: true, awaitingCount: 1, announcementKey: 'p-c' })
    expect(wrapper.get('[data-testid="paper-review-queue-announcement"]').text()).toContain(
      '1 proposal awaiting review',
    )
    expect(wrapper.get('[data-testid="paper-review-queue-live"]').element).toBe(region)
  })

  it('replaces the announcement node when the queue identity changes under an unchanged count (#2214 item 4)', async () => {
    // The rail cannot derive this itself: `items` is the whole visible queue,
    // not the awaiting set the count is about, and a rail-local derivation is
    // exactly how the two skins drift (#1124 / ADR-0038). The key comes from the
    // shared composable so Legacy and Paper re-announce on the same evidence.
    const wrapper = mountRail({
      awaitingCount: 2,
      queueScopeLoaded: true,
      announcementKey: 'p-a\np-b',
    })
    const region = wrapper.get('[data-testid="paper-review-queue-live"]').element
    const announced = wrapper.get('[data-testid="paper-review-queue-announcement"]')
    expect(announced.text()).toContain('2 proposals awaiting review')
    const before = announced.element

    // A byte-identical queue is not news.
    await wrapper.setProps({ announcementKey: 'p-a\np-b' })
    expect(wrapper.get('[data-testid="paper-review-queue-announcement"]').element).toBe(before)

    // One awaiting proposal swapped for another: same count, same sentence.
    await wrapper.setProps({ announcementKey: 'p-a\np-c' })
    const after = wrapper.get('[data-testid="paper-review-queue-announcement"]')
    expect(after.text()).toContain('2 proposals awaiting review')
    expect(after.element).not.toBe(before)
    // The region is never remounted; only the node inside it is replaced.
    expect(wrapper.get('[data-testid="paper-review-queue-live"]').element).toBe(region)
  })

  it('keeps the whole announcement withheld while the count is unspeakable, key or no key', async () => {
    // The identity moves for a reason that is not "the awaiting queue changed"
    // when the queue is withdrawn: `recordQueueAccessRevoked` empties it. The
    // #2593 gate still wins over the re-announcement.
    const wrapper = mountRail({
      awaitingCount: 2,
      queueScopeLoaded: true,
      announcementKey: 'p-a\np-b',
    })
    await wrapper.setProps({ queueUnavailable: true, awaitingCount: 0, announcementKey: '' })
    const live = wrapper.get('[data-testid="paper-review-queue-live"]')
    expect(live.text()).toBe('')
    expect(wrapper.find('[data-testid="paper-review-queue-announcement"]').exists()).toBe(false)
  })
})

describe('ReviewQueueRail focus handoff (#2599 item 2)', () => {
  it('focuses the first queue row on request and reports that it did', () => {
    // The queue's own rows are the target the unavailable-pin panel hands focus
    // to. The rail owns them, so it owns the handoff: a parent reaching into
    // this subtree would be the drift seam (#1124 / ADR-0038).
    const wrapper = mountRail({
      attachTo: true,
      items: [makeItem({ id: 'p-1', serial: '#0001' }), makeItem({ id: 'p-2', serial: '#0002' })],
    })
    try {
      expect(wrapper.vm.focusFirstQueueRow()).toBe(true)
      const first = wrapper.get('.paper-review-rail__queue-row button').element
      expect(document.activeElement).toBe(first)
      expect(first.getAttribute('data-serial')).toBe('#0001')
    } finally {
      wrapper.unmount()
    }
  })

  it('reports that it did not when the queue has no rows', () => {
    // The caller needs the honest answer: an empty queue has no row to hold
    // focus, and the panel's own empty state must take it instead.
    const wrapper = mountRail({ attachTo: true, items: [] })
    try {
      expect(wrapper.vm.focusFirstQueueRow()).toBe(false)
    } finally {
      wrapper.unmount()
    }
  })
})
