import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'

import InboxDetailPanel from '../../../components/inbox/InboxDetailPanel.vue'
import type { CaptureItem, CaptureStatusValue } from '../../../types/capture'

/**
 * Legacy Inbox detail panel — degraded-triage notice (#2202).
 *
 * Three states, one surface: a clean success renders nothing new, a degraded
 * success renders a caution that is NOT an alert, and a genuine failure keeps
 * the error banner it always had. The middle one is the defect; the outer two
 * are the guard rails that keep the fix from widening into the failure gate.
 */

const NOTICE =
  'LLM triage unavailable (ProviderDegraded); using deterministic extractor. Live provider request failed.'

function makeItem(
  status: CaptureStatusValue,
  errorMessage: string | null,
): CaptureItem {
  return {
    id: 'capture-1',
    userId: 'user-1',
    boardId: 'board-alpha',
    status,
    source: 'TranscriptPaste',
    rawText: 'Rosa: standup at nine. Sam: I will ship the export by Friday.',
    textExcerpt: 'Rosa: standup at nine.',
    createdAt: new Date('2026-08-29T09:42:00Z').toISOString(),
    processedAt: new Date('2026-08-29T09:42:10Z').toISOString(),
    retryCount: 0,
    errorMessage,
    provenance: null,
    canEditSuggestion: false,
  } as CaptureItem
}

function mountPanel(item: CaptureItem) {
  return mount(InboxDetailPanel, {
    props: {
      selectedItemId: item.id,
      selectedItem: item,
      hashLoadFailedItemId: null,
      loadingDetail: false,
      actionBusyItemId: null,
      triagePollingItemId: null,
      isEditingSuggestion: false,
      editedText: '',
      editedTitleHint: '',
    },
  })
}

describe('InboxDetailPanel — degraded triage notice', () => {
  it('renders nothing new for a clean successful capture', () => {
    const wrapper = mountPanel(makeItem('ProposalCreated', null))

    expect(wrapper.find('[data-testid="capture-degraded-notice"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="capture-error-banner"]').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('Triaged without the model')
  })

  it('renders the notice on a degraded successful capture, verbatim and in the caution tone', () => {
    const wrapper = mountPanel(makeItem('ProposalCreated', NOTICE))

    const notice = wrapper.find('[data-testid="capture-degraded-notice"]')
    expect(notice.exists()).toBe(true)

    // Announced as a status, never as an alert (the acceptance criterion that
    // separates this from the failure banner for a screen-reader user).
    expect(notice.attributes('role')).toBe('status')

    // Tone: the note class, not the error banner's.
    expect(notice.classes()).toContain('td-inbox-detail__degraded')
    expect(wrapper.find('[data-testid="capture-error-banner"]').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('Triage failed')

    // Copy names the producer and what the fallback means for review.
    expect(notice.text()).toContain('Triaged without the model')
    expect(notice.text()).toContain("Taskdeck's deterministic offline extractor")
    expect(notice.text()).toContain('no evidence links')

    // The server's own words, unedited and with nothing appended.
    const reason = wrapper.find('[data-testid="capture-degraded-reason"]')
    expect(reason.text()).toBe(`Reported: ${NOTICE}`)
  })

  it('renders the notice for a degraded capture that triaged with nothing to propose', () => {
    const wrapper = mountPanel(makeItem('Triaged', NOTICE))

    expect(wrapper.find('[data-testid="capture-degraded-notice"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="capture-error-banner"]').exists()).toBe(false)
  })

  it('keeps the existing error rendering for a failed capture and adds no caution notice', () => {
    const wrapper = mountPanel(makeItem('Failed', 'Triage failed: the capture text was unusable.'))

    const banner = wrapper.find('[data-testid="capture-error-banner"]')
    expect(banner.exists()).toBe(true)
    expect(banner.text()).toContain('Triage failed')
    expect(banner.text()).toContain('Triage failed: the capture text was unusable.')

    expect(wrapper.find('[data-testid="capture-degraded-notice"]').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('Triaged without the model')
  })

  it('keeps the numeric Failed status on the error path', () => {
    const wrapper = mountPanel(makeItem(6, 'Triage failed: the capture text was unusable.'))

    expect(wrapper.find('[data-testid="capture-error-banner"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="capture-degraded-notice"]').exists()).toBe(false)
  })
})
