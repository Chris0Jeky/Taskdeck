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
    who: 'haiku',
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
}>) {
  return mount(ReviewQueueRail, {
    props: {
      items: props?.items ?? [makeItem()],
      activeId: props?.activeId ?? null,
      awaitingCount: 3,
      staleCount: 2,
      recentlyApplied: props?.recentlyApplied ?? [],
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

  it('renders recently-applied rows when provided', () => {
    const wrapper = mountRail({
      recentlyApplied: [
        { id: 'recent-1', serial: '#R01', title: 'Move done', left: '5h 48m', expired: false },
        { id: 'recent-2', serial: '#R02', title: 'Old apply', left: null, expired: true },
      ],
    })
    expect(wrapper.text()).toContain('5h 48m')
    expect(wrapper.text()).toContain('sealed')
  })
})
