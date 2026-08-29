import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'

import PaperTriageTable from '../../../../views/paper/inbox/PaperTriageTable.vue'
import type { CaptureItemSummary, CaptureStatusValue } from '../../../../types/capture'

/**
 * Paper triage table — degraded-triage notice (#2202).
 *
 * Same three states as the Legacy panel, plus the tone assertion that matters
 * most on this surface: the pre-existing `failureReason` span lives INSIDE the
 * row button with `role="alert"` in the overdue colour, and the new notice must
 * not be that. Paper has no capture detail panel outside read-only history, so
 * the row is the only place a Paper user can be told.
 */

const NOTICE =
  'LLM triage unavailable (ProviderDegraded); using deterministic extractor. Live provider request failed.'

const mockBoardStore = reactive({
  boards: [{ id: 'board-alpha', name: 'Alpha' }] as { id: string; name: string }[],
  fetchBoards: vi.fn<() => Promise<void>>(),
})

vi.mock('../../../../store/boardStore', () => ({
  useBoardStore: () => mockBoardStore,
}))

const mockCaptureStore = {
  fetchDetail: vi.fn<(...args: unknown[]) => Promise<unknown>>(),
  updateSuggestion: vi.fn<(...args: unknown[]) => Promise<unknown>>(),
}

vi.mock('../../../../store/captureStore', () => ({
  useCaptureStore: () => mockCaptureStore,
}))

function makeItem(
  status: CaptureStatusValue,
  errorMessage: string | null,
): CaptureItemSummary {
  return {
    id: 'capture-1',
    userId: 'user-1',
    boardId: 'board-alpha',
    status,
    source: 'TranscriptPaste',
    textExcerpt: 'Rosa: standup at nine.',
    createdAt: new Date('2026-08-29T09:42:00Z').toISOString(),
    processedAt: new Date('2026-08-29T09:42:10Z').toISOString(),
    errorMessage,
  } as CaptureItemSummary
}

function mountTable(item: CaptureItemSummary) {
  return mount(PaperTriageTable, { props: { items: [item] } })
}

describe('PaperTriageTable — degraded triage notice', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockBoardStore.fetchBoards.mockResolvedValue(undefined)
    mockCaptureStore.fetchDetail.mockResolvedValue(null)
  })

  it('renders nothing new for a clean successful capture', () => {
    const wrapper = mountTable(makeItem('ProposalCreated', null))

    expect(wrapper.find('[data-testid="capture-degraded-notice"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="capture-failure-reason"]').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('Triaged without the model')
  })

  it('renders the notice on a degraded successful capture, verbatim and in the caution tone', () => {
    const wrapper = mountTable(makeItem('ProposalCreated', NOTICE))

    const notice = wrapper.find('[data-testid="capture-degraded-notice"]')
    expect(notice.exists()).toBe(true)

    // A status, not an alert — the row succeeded.
    expect(notice.attributes('role')).toBe('status')
    expect(notice.classes()).toContain('paper-triage__degraded')

    // The failure rendering stays absent, and so does its wording.
    expect(wrapper.find('[data-testid="capture-failure-reason"]').exists()).toBe(false)
    expect(wrapper.find('.paper-triage__reason').exists()).toBe(false)

    // Copy names the producer and what the fallback means for review.
    expect(notice.text()).toContain('Triaged without the model')
    expect(notice.text()).toContain("Taskdeck's deterministic offline extractor")
    expect(notice.text()).toContain('no evidence links')

    // The server's own words, unedited and with nothing appended.
    expect(wrapper.find('[data-testid="capture-degraded-reason"]').text()).toBe(
      `Reported: ${NOTICE}`,
    )
  })

  it('associates the notice with the row it describes', () => {
    const wrapper = mountTable(makeItem('ProposalCreated', NOTICE))

    const noticeId = wrapper.find('[data-testid="capture-degraded-notice"]').attributes('id')
    expect(noticeId).toBeTruthy()
    expect(wrapper.find('.paper-triage__open').attributes('aria-describedby')).toBe(noticeId)
  })

  it('renders the notice for a degraded capture that triaged with nothing to propose', () => {
    const wrapper = mountTable(makeItem('Triaged', NOTICE))

    expect(wrapper.find('[data-testid="capture-degraded-notice"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="capture-failure-reason"]').exists()).toBe(false)
  })

  it('keeps the existing failure rendering for a failed capture and adds no caution notice', () => {
    const wrapper = mountTable(makeItem('Failed', 'Triage failed: the capture text was unusable.'))

    const reason = wrapper.find('[data-testid="capture-failure-reason"]')
    expect(reason.exists()).toBe(true)
    expect(reason.attributes('role')).toBe('alert')
    expect(reason.text()).toBe('Triage failed: the capture text was unusable.')

    expect(wrapper.find('[data-testid="capture-degraded-notice"]').exists()).toBe(false)
    expect(wrapper.find('.paper-triage__open').attributes('aria-describedby')).toBeUndefined()
    expect(wrapper.text()).not.toContain('Triaged without the model')
  })

  it('keeps the numeric Failed status on the failure path', () => {
    const wrapper = mountTable(makeItem(6, 'Triage failed: the capture text was unusable.'))

    expect(wrapper.find('[data-testid="capture-failure-reason"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="capture-degraded-notice"]').exists()).toBe(false)
  })
})
