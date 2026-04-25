import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import PaperCardDetailView from '../../../views/paper/PaperCardDetailView.vue'
import type { Card, CardCaptureProvenance } from '../../../types/board'

function makeCard(overrides: Partial<Card> = {}): Card {
  return {
    id: 'c-1',
    boardId: 'b-1',
    columnId: 'col-1',
    title: 'Implement dark mode',
    description: 'Apply Paper-at-Night tokens across all surfaces.',
    dueDate: null,
    isBlocked: false,
    blockReason: null,
    position: 0,
    labels: [],
    createdAt: '2026-04-23T10:00:00.000Z',
    updatedAt: '2026-04-23T10:00:00.000Z',
    ...overrides,
  }
}

describe('PaperCardDetailView', () => {
  it('renders a smoke-friendly surface for a stubbed card', () => {
    const wrapper = mount(PaperCardDetailView, {
      props: { card: makeCard(), serial: 'C-090', statusLabel: 'in progress' },
    })
    expect(wrapper.find('[data-paper-card-detail]').exists()).toBe(true)
    expect(wrapper.find('.paper-card-detail__title').text()).toBe('Implement dark mode')
    expect(wrapper.find('.paper-card-detail__eyebrow').text()).toContain('C-090')
    expect(wrapper.find('.paper-card-detail__eyebrow').text()).toContain('in progress')
    // No proposal → banner hidden.
    expect(wrapper.find('[data-pending-proposal]').exists()).toBe(false)
  })

  it('shows the pending proposal banner when provenance.proposalStatus is PendingReview', () => {
    const provenance: CardCaptureProvenance = {
      cardId: 'c-1',
      captureItemId: 'cap-1',
      proposalId: 'p-9',
      proposalStatus: 'PendingReview',
      triageRunId: null,
    }
    const wrapper = mount(PaperCardDetailView, {
      props: { card: makeCard(), provenance },
    })
    const banner = wrapper.find('[data-pending-proposal]')
    expect(banner.exists()).toBe(true)
    expect(banner.text()).toContain('Pending proposal')
  })

  it('emits open-proposal with the proposal id when the banner CTA is clicked', async () => {
    const provenance: CardCaptureProvenance = {
      cardId: 'c-1',
      captureItemId: 'cap-1',
      proposalId: 'p-9',
      proposalStatus: 'PendingReview',
      triageRunId: null,
    }
    const wrapper = mount(PaperCardDetailView, {
      props: { card: makeCard(), provenance },
    })
    const cta = wrapper.find('[data-pending-proposal] button')
    expect(cta.exists()).toBe(true)
    await cta.trigger('click')
    expect(wrapper.emitted('open-proposal')?.[0]).toEqual(['p-9'])
  })

  it('hides the banner when the proposal has already been Applied', () => {
    const provenance: CardCaptureProvenance = {
      cardId: 'c-1',
      captureItemId: 'cap-1',
      proposalId: 'p-9',
      proposalStatus: 'Applied',
      triageRunId: null,
    }
    const wrapper = mount(PaperCardDetailView, {
      props: { card: makeCard(), provenance },
    })
    expect(wrapper.find('[data-pending-proposal]').exists()).toBe(false)
  })

  it('renders subtasks via PaperSubtaskLedger and forwards toggle events', async () => {
    const wrapper = mount(PaperCardDetailView, {
      props: {
        card: makeCard(),
        subtasks: [
          { id: 's-1', label: 'Migrate token sheet', done: true },
          { id: 's-2', label: 'Audit AA contrast', done: false },
        ],
      },
    })
    const ledger = wrapper.find('[data-paper-subtask-ledger]')
    expect(ledger.exists()).toBe(true)
    const rows = ledger.findAll('li')
    expect(rows.length).toBe(2)
    await rows[1].find('button').trigger('click')
    expect(wrapper.emitted('toggle-subtask')?.[0]).toEqual(['s-2'])
  })
})
