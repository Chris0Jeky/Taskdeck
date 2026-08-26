import { describe, expect, it } from 'vitest'
import { ref } from 'vue'
import { isPendingTriageStatus, useInboxCounts } from '../../composables/useInboxCounts'
import type { CaptureItemSummary, CaptureStatusValue } from '../../types/capture'

/**
 * One definition behind both inbox counters (#1974).
 *
 * The badge and the Inbox header disagreed because they counted different
 * things while both were called a queue. `isPendingTriageStatus` is the
 * client-side mirror of the server's `capturesNeedingTriage`
 * (`NewCount + FailedCount` in `WorkspaceService`); these assertions pin that
 * correspondence so the two sides cannot drift apart again silently.
 */

function item(id: string, status: CaptureStatusValue): CaptureItemSummary {
  return {
    id,
    userId: 'u1',
    boardId: null,
    status,
    source: 'Typed',
    textExcerpt: id,
    createdAt: new Date().toISOString(),
    processedAt: null,
  }
}

describe('isPendingTriageStatus', () => {
  it.each([
    ['New', true],
    ['Failed', true],
    ['Triaging', false],
    ['Triaged', false],
    ['ProposalCreated', false],
    ['Converted', false],
    ['Ignored', false],
  ] as Array<[CaptureStatusValue, boolean]>)('treats %s as pending=%s', (status, expected) => {
    expect(isPendingTriageStatus(status)).toBe(expected)
  })

  it.each([
    [0, true],
    [6, true],
    [1, false],
    [2, false],
    [3, false],
    [4, false],
    [5, false],
  ] as Array<[CaptureStatusValue, boolean]>)(
    'treats the ordinal form %s as pending=%s',
    (status, expected) => {
      expect(isPendingTriageStatus(status)).toBe(expected)
    },
  )
})

describe('useInboxCounts', () => {
  it('counts pending and total separately for a mixed inbox', () => {
    // The reported divergence: five captures on screen, only two pending.
    const items = ref<CaptureItemSummary[]>([
      item('a', 'New'),
      item('b', 'Failed'),
      item('c', 'Converted'),
      item('d', 'ProposalCreated'),
      item('e', 'Ignored'),
    ])

    const { pendingTriageCount, capturedCount } = useInboxCounts(items)

    expect(pendingTriageCount.value).toBe(2)
    expect(capturedCount.value).toBe(5)
  })

  it('does not let applied captures inflate the pending count', () => {
    const items = ref<CaptureItemSummary[]>([
      item('a', 'Converted'),
      item('b', 'Converted'),
      item('c', 'Converted'),
    ])

    const { pendingTriageCount, capturedCount } = useInboxCounts(items)

    expect(pendingTriageCount.value).toBe(0)
    expect(capturedCount.value).toBe(3)
  })

  it.each(['Kept', 0] as const)('does not count a kept capture with disposition %s as pending', (kind) => {
    const kept = item('kept', 'New')
    kept.disposition = {
      kind,
      at: new Date().toISOString(),
      byUserId: 'u1',
      boardId: null,
    }

    const { pendingTriageCount, capturedCount } = useInboxCounts(ref([kept]))

    expect(pendingTriageCount.value).toBe(0)
    expect(capturedCount.value).toBe(1)
  })

  it('tracks later mutations of the source list', () => {
    const items = ref<CaptureItemSummary[]>([item('a', 'New')])
    const { pendingTriageCount, capturedCount } = useInboxCounts(items)

    expect(pendingTriageCount.value).toBe(1)

    items.value = [item('a', 'Triaging'), item('b', 'New')]

    expect(pendingTriageCount.value).toBe(1)
    expect(capturedCount.value).toBe(2)
  })

  it('reports zero for an empty inbox', () => {
    const { pendingTriageCount, capturedCount } = useInboxCounts(ref<CaptureItemSummary[]>([]))

    expect(pendingTriageCount.value).toBe(0)
    expect(capturedCount.value).toBe(0)
  })
})
