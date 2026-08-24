import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import ReviewQueueRail, {
  type QueueFilter,
  type QueueRailItem,
} from '../../../../views/paper/review/ReviewQueueRail.vue'

/**
 * #1940 — the All/Mine author partition is withdrawn only from the
 * server-computed collaboration-membership contract, and Stale always survives.
 *
 * Kept in its own file so the membership rule is legible on its own and does
 * not entangle with the broader queue-rail suite.
 */

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

function mountRail(props: { items?: QueueRailItem[]; authorPartitionAvailable?: boolean } = {}) {
  return mount(ReviewQueueRail, {
    props: {
      items: props.items ?? [makeItem()],
      activeId: null,
      awaitingCount: 3,
      staleCount: 2,
      recentlyApplied: [],
      ...(props.authorPartitionAvailable === undefined
        ? {}
        : { authorPartitionAvailable: props.authorPartitionAvailable }),
    },
  })
}

function pillLabels(wrapper: ReturnType<typeof mountRail>): string[] {
  return wrapper.findAll('.paper-review-rail__pill').map((pill) => pill.text())
}

function emittedFilters(wrapper: ReturnType<typeof mountRail>): QueueFilter[] {
  return (wrapper.emitted('filter-change') ?? []).map((args) => args[0] as QueueFilter)
}

describe('ReviewQueueRail — All/Mine membership gate', () => {
  it('shows All, Mine and Stale by default, so an unwired caller loses nothing', () => {
    expect(pillLabels(mountRail())).toEqual(['All', 'Mine', 'Stale'])
  })

  it('keeps All and Mine while membership is unknown or still loading', () => {
    // `authorPartitionAvailable` is what the view computes from the contract;
    // anything other than a positive solo answer must leave it true.
    expect(pillLabels(mountRail({ authorPartitionAvailable: true }))).toEqual([
      'All',
      'Mine',
      'Stale',
    ])
  })

  it('hides All and Mine but preserves Stale on a proven single-member workspace', () => {
    expect(pillLabels(mountRail({ authorPartitionAvailable: false }))).toEqual(['Stale'])
  })

  it('does not select a filter merely because the partition is hidden', () => {
    const wrapper = mountRail({ authorPartitionAvailable: false })

    expect(wrapper.find('.paper-review-rail__pill--active').exists()).toBe(false)
    expect(emittedFilters(wrapper)).toEqual([])
  })

  it('falls back from Mine to the full queue when the partition collapses under it', async () => {
    const wrapper = mountRail({
      items: [makeItem({ id: 'a', mine: true }), makeItem({ id: 'b', mine: false })],
      authorPartitionAvailable: true,
    })

    await wrapper.findAll('.paper-review-rail__pill')[1].trigger('click')
    expect(emittedFilters(wrapper)).toEqual(['mine'])

    await wrapper.setProps({ authorPartitionAvailable: false })

    expect(emittedFilters(wrapper)).toEqual(['mine', 'all'])
    expect(pillLabels(wrapper)).toEqual(['Stale'])
    expect(wrapper.find('.paper-review-rail__pill--active').exists()).toBe(false)
    expect(wrapper.findAll('.paper-review-q').length).toBe(2)
  })

  it('leaves a Stale selection alone when the partition collapses', async () => {
    const wrapper = mountRail({
      items: [makeItem({ id: 'a', stale: true }), makeItem({ id: 'b' })],
      authorPartitionAvailable: true,
    })

    await wrapper.findAll('.paper-review-rail__pill')[2].trigger('click')
    await wrapper.setProps({ authorPartitionAvailable: false })

    expect(emittedFilters(wrapper)).toEqual(['stale'])
    expect(pillLabels(wrapper)).toEqual(['Stale'])
    expect(wrapper.find('.paper-review-rail__pill--active').exists()).toBe(true)
    expect(wrapper.findAll('.paper-review-q').length).toBe(1)
  })

  it('makes the lone Stale chip a toggle so the reviewer can reach the whole queue again', async () => {
    const wrapper = mountRail({
      items: [makeItem({ id: 'a', stale: true }), makeItem({ id: 'b' })],
      authorPartitionAvailable: false,
    })

    const stale = () => wrapper.findAll('.paper-review-rail__pill')[0]

    await stale().trigger('click')
    expect(emittedFilters(wrapper)).toEqual(['stale'])
    expect(wrapper.findAll('.paper-review-q').length).toBe(1)

    await stale().trigger('click')
    expect(emittedFilters(wrapper)).toEqual(['stale', 'all'])
    expect(wrapper.findAll('.paper-review-q').length).toBe(2)
    expect(stale().attributes('aria-pressed')).toBe('false')
  })

  it('keeps Stale a one-way switch while the partition is available', async () => {
    const wrapper = mountRail({
      items: [makeItem({ id: 'a', stale: true }), makeItem({ id: 'b' })],
      authorPartitionAvailable: true,
    })

    const stale = () => wrapper.findAll('.paper-review-rail__pill')[2]
    await stale().trigger('click')
    await stale().trigger('click')

    expect(emittedFilters(wrapper)).toEqual(['stale', 'stale'])
    expect(stale().attributes('aria-pressed')).toBe('true')
  })

  it('restores All and Mine when membership stops being solo', async () => {
    const wrapper = mountRail({ authorPartitionAvailable: false })

    await wrapper.setProps({ authorPartitionAvailable: true })

    expect(pillLabels(wrapper)).toEqual(['All', 'Mine', 'Stale'])
  })
})
