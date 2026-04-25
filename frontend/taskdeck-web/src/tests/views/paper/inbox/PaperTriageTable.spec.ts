import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import PaperTriageTable from '../../../../views/paper/inbox/PaperTriageTable.vue'
import type { CaptureItemSummary } from '../../../../types/capture'

function makeItems(): CaptureItemSummary[] {
  const createdAt = new Date('2026-04-25T09:42:00Z').toISOString()
  return [
    {
      id: 'capture-1',
      userId: 'user-1',
      boardId: null,
      status: 'New',
      source: 'Typed',
      textExcerpt: 'First excerpt',
      createdAt,
      processedAt: null,
    },
    {
      id: 'capture-2',
      userId: 'user-1',
      boardId: null,
      status: 'Triaging',
      source: 'Paste',
      textExcerpt: 'Second excerpt',
      createdAt,
      processedAt: null,
    },
  ] as CaptureItemSummary[]
}

describe('PaperTriageTable', () => {
  it('renders an empty state when there are no items', () => {
    const wrapper = mount(PaperTriageTable, { props: { items: [] } })
    expect(wrapper.text()).toContain('No captures yet')
    expect(wrapper.find('.paper-triage__list').exists()).toBe(false)
  })

  it('surfaces list errors with a retry action instead of the empty state', async () => {
    const wrapper = mount(PaperTriageTable, {
      props: { items: [], listError: 'Failed to load captures' },
    })

    expect(wrapper.find('[role="alert"]').text()).toContain('Failed to load captures')
    expect(wrapper.text()).not.toContain('A pen and a phrase')
    await wrapper.find('.paper-triage__retry').trigger('click')
    expect(wrapper.emitted('retry')).toHaveLength(1)
  })

  it('surfaces list errors above stale rows when refresh fails after prior data', () => {
    const wrapper = mount(PaperTriageTable, {
      props: { items: makeItems(), listError: 'Refresh failed' },
    })

    expect(wrapper.find('[role="alert"]').text()).toContain('Refresh failed')
    expect(wrapper.findAll('.paper-triage__row')).toHaveLength(2)
    expect(wrapper.text()).not.toContain('A pen and a phrase')
  })

  it('prioritizes failed/error status tone over triage wording', () => {
    const items = makeItems()
    items[0] = { ...items[0], status: 'Triage Failed' }
    const wrapper = mount(PaperTriageTable, { props: { items } })

    expect(wrapper.find('.tagstamp').attributes('data-tone')).toBe('overdue')
  })

  it('emits accept with the item id when the Accept button is clicked', async () => {
    const wrapper = mount(PaperTriageTable, { props: { items: makeItems() } })
    const acceptBtn = wrapper.findAll('button[data-action="accept"]')[0]
    await acceptBtn.trigger('click')
    const events = wrapper.emitted('accept')
    expect(events).toBeDefined()
    expect(events?.[0]).toEqual(['capture-1'])
  })

  it('emits reject with the item id when the Reject button is clicked', async () => {
    const wrapper = mount(PaperTriageTable, { props: { items: makeItems() } })
    const rejectBtn = wrapper.findAll('button[data-action="reject"]')[0]
    await rejectBtn.trigger('click')
    const events = wrapper.emitted('reject')
    expect(events).toBeDefined()
    expect(events?.[0]).toEqual(['capture-1'])
  })

  it('disables row actions for immutable capture statuses', async () => {
    const items = makeItems()
    items[1] = { ...items[1], status: 'ProposalCreated' }
    const wrapper = mount(PaperTriageTable, { props: { items } })

    const acceptBtn = wrapper.findAll('button[data-action="accept"]')[1]
    const rejectBtn = wrapper.findAll('button[data-action="reject"]')[1]

    expect(acceptBtn.attributes('disabled')).toBeDefined()
    expect(rejectBtn.attributes('disabled')).toBeDefined()

    await acceptBtn.trigger('click')
    await rejectBtn.trigger('click')
    expect(wrapper.emitted('accept')).toBeUndefined()
    expect(wrapper.emitted('reject')).toBeUndefined()
  })

  it('idempotent: double-click on Accept fires once when busy guard is in place', async () => {
    const wrapper = mount(PaperTriageTable, {
      props: {
        items: makeItems(),
        actionBusyItemId: null,
      },
    })
    const acceptBtn = wrapper.findAll('button[data-action="accept"]')[0]
    // First click — fires.
    await acceptBtn.trigger('click')
    // Caller flips actionBusyItemId so the row is now busy; the button is
    // disabled and a second click (whether via DOM or via the prop guard)
    // must not fire a second emit.
    await wrapper.setProps({
      items: makeItems(),
      actionBusyItemId: 'capture-1',
    })
    await acceptBtn.trigger('click')
    const events = wrapper.emitted('accept')
    expect(events).toHaveLength(1)
  })

  it('disables both action buttons while a row is busy', async () => {
    const wrapper = mount(PaperTriageTable, {
      props: {
        items: makeItems(),
        actionBusyItemId: 'capture-1',
      },
    })
    const acceptBtn = wrapper.findAll('button[data-action="accept"]')[0]
    const rejectBtn = wrapper.findAll('button[data-action="reject"]')[0]
    expect(acceptBtn.attributes('disabled')).toBeDefined()
    expect(rejectBtn.attributes('disabled')).toBeDefined()
  })

  it('disables every row action while any triage action is in flight', async () => {
    const wrapper = mount(PaperTriageTable, {
      props: {
        items: makeItems(),
        actionBusyItemId: 'capture-1',
      },
    })
    const secondAcceptBtn = wrapper.findAll('button[data-action="accept"]')[1]
    const secondRejectBtn = wrapper.findAll('button[data-action="reject"]')[1]

    expect(secondAcceptBtn.attributes('disabled')).toBeDefined()
    expect(secondRejectBtn.attributes('disabled')).toBeDefined()

    await secondAcceptBtn.trigger('click')
    await secondRejectBtn.trigger('click')
    expect(wrapper.emitted('accept')).toBeUndefined()
    expect(wrapper.emitted('reject')).toBeUndefined()
  })

  it('emits open when an item row excerpt is clicked', async () => {
    const wrapper = mount(PaperTriageTable, { props: { items: makeItems() } })
    const opener = wrapper.findAll('.paper-triage__open')[1]
    await opener.trigger('click')
    expect(wrapper.emitted('open')?.[0]).toEqual(['capture-2'])
  })
})
