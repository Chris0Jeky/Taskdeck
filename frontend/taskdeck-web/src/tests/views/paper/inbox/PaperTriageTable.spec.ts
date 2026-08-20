import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import PaperTriageTable from '../../../../views/paper/inbox/PaperTriageTable.vue'
import type { CaptureItemSummary, CaptureStatusValue } from '../../../../types/capture'

type MockBoard = { id: string; name: string; canWrite?: boolean }

const defaultBoards = (): MockBoard[] => [
  { id: 'board-alpha', name: 'Alpha' },
  { id: 'board-beta', name: 'Beta' },
]

const mockBoardStore = reactive({
  boards: defaultBoards() as MockBoard[],
  fetchBoards: vi.fn<() => Promise<void>>(),
})

vi.mock('../../../../store/boardStore', () => ({
  useBoardStore: () => mockBoardStore,
}))

function makeItems(): CaptureItemSummary[] {
  const createdAt = new Date('2026-04-25T09:42:00Z').toISOString()
  return [
    {
      id: 'capture-1',
      userId: 'user-1',
      boardId: 'board-alpha',
      status: 'New',
      source: 'Typed',
      textExcerpt: 'First excerpt',
      createdAt,
      processedAt: null,
    },
    {
      id: 'capture-2',
      userId: 'user-1',
      boardId: 'board-alpha',
      status: 'Triaging',
      source: 'Paste',
      textExcerpt: 'Second excerpt',
      createdAt,
      processedAt: null,
    },
  ] as CaptureItemSummary[]
}

describe('PaperTriageTable', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockBoardStore.boards = defaultBoards()
    mockBoardStore.fetchBoards.mockResolvedValue(undefined)
  })

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
    // Deliberately outside the CaptureStatus union: this asserts statusTone()'s ordering
    // (failed/error wins over triage wording) for an unrecognised status string from the server.
    const outOfContractStatus = 'Triage Failed' as unknown as CaptureStatusValue
    items[0] = { ...items[0], status: outOfContractStatus }
    const wrapper = mount(PaperTriageTable, { props: { items } })

    expect(wrapper.find('.tagstamp').attributes('data-tone')).toBe('overdue')
  })

  it('renders readable labels and tones for numeric capture enum values', () => {
    const items = makeItems()
    items[0] = { ...items[0], status: 3, source: 2 }
    items[1] = { ...items[1], status: 6, source: 5 }
    const wrapper = mount(PaperTriageTable, { props: { items } })
    const tags = wrapper.findAll('.tagstamp')

    expect(tags[0].text()).toBe('Ready for review')
    expect(tags[0].attributes('data-tone')).toBe('ember')
    expect(tags[1].text()).toBe('Transcript')
    expect(tags[2].text()).toBe('Failed')
    expect(tags[2].attributes('data-tone')).toBe('overdue')
    expect(tags[3].text()).toBe('Meeting')
  })

  it('emits accept with the item id and its board when the Accept button is clicked', async () => {
    const wrapper = mount(PaperTriageTable, { props: { items: makeItems() } })
    const acceptBtn = wrapper.findAll('button[data-action="accept"]')[0]
    await acceptBtn.trigger('click')
    const events = wrapper.emitted('accept')
    expect(events).toBeDefined()
    expect(events?.[0]).toEqual(['capture-1', 'board-alpha'])
  })

  it('emits reject with the item id when the Reject button is clicked', async () => {
    const wrapper = mount(PaperTriageTable, { props: { items: makeItems() } })
    const rejectBtn = wrapper.findAll('button[data-action="reject"]')[0]
    await rejectBtn.trigger('click')
    const events = wrapper.emitted('reject')
    expect(events).toBeDefined()
    expect(events?.[0]).toEqual(['capture-1'])
  })

  it('requires a board before accepting a board-less capture (#1764)', async () => {
    const items = makeItems()
    items[0] = { ...items[0], boardId: null }
    const wrapper = mount(PaperTriageTable, { props: { items } })

    // Accept must NOT emit immediately — it reveals the inline board picker instead.
    await wrapper.findAll('button[data-action="accept"]')[0].trigger('click')
    expect(wrapper.emitted('accept')).toBeUndefined()
    const picker = wrapper.find('[data-testid="capture-board-pick"]')
    expect(picker.exists()).toBe(true)

    // Choosing a board then confirming emits accept with the chosen board id.
    await picker.find('select').setValue('board-beta')
    await wrapper.find('button[data-action="accept-on-board"]').trigger('click')
    expect(wrapper.emitted('accept')?.[0]).toEqual(['capture-1', 'board-beta'])
    // Picker closes after confirming.
    expect(wrapper.find('[data-testid="capture-board-pick"]').exists()).toBe(false)
  })

  it('cannot confirm the board picker without choosing a board', async () => {
    const items = makeItems()
    items[0] = { ...items[0], boardId: null }
    const wrapper = mount(PaperTriageTable, { props: { items } })

    await wrapper.findAll('button[data-action="accept"]')[0].trigger('click')
    const confirmBtn = wrapper.find('button[data-action="accept-on-board"]')
    expect(confirmBtn.attributes('disabled')).toBeDefined()
    await confirmBtn.trigger('click')
    expect(wrapper.emitted('accept')).toBeUndefined()

    // Cancel dismisses the picker without emitting.
    await wrapper.find('button[data-action="cancel-board-pick"]').trigger('click')
    expect(wrapper.find('[data-testid="capture-board-pick"]').exists()).toBe(false)
    expect(wrapper.emitted('accept')).toBeUndefined()
  })

  it('surfaces the failure reason on a failed capture', () => {
    const items = makeItems()
    items[0] = {
      ...items[0],
      status: 'Failed',
      errorMessage: 'BoardId is required to triage capture items into proposals',
    }
    const wrapper = mount(PaperTriageTable, { props: { items } })
    const reason = wrapper.find('[data-testid="capture-failure-reason"]')
    expect(reason.exists()).toBe(true)
    expect(reason.text()).toContain('BoardId is required')
  })

  it('does not surface an error line for a non-failed capture', () => {
    const items = makeItems()
    items[0] = { ...items[0], status: 'New', errorMessage: 'stale message' }
    const wrapper = mount(PaperTriageTable, { props: { items } })
    expect(wrapper.find('[data-testid="capture-failure-reason"]').exists()).toBe(false)
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

  it('keeps other mutable rows actionable while triage polling is active', async () => {
    const items = makeItems()
    items[1] = { ...items[1], status: 'New' }
    const wrapper = mount(PaperTriageTable, {
      props: {
        items,
        triagePollingItemId: 'capture-1',
      },
    })
    const firstAcceptBtn = wrapper.findAll('button[data-action="accept"]')[0]
    const firstRejectBtn = wrapper.findAll('button[data-action="reject"]')[0]
    const secondAcceptBtn = wrapper.findAll('button[data-action="accept"]')[1]
    const secondRejectBtn = wrapper.findAll('button[data-action="reject"]')[1]

    expect(firstAcceptBtn.attributes('disabled')).toBeDefined()
    expect(firstRejectBtn.attributes('disabled')).toBeDefined()
    expect(secondAcceptBtn.attributes('disabled')).toBeUndefined()
    expect(secondRejectBtn.attributes('disabled')).toBeUndefined()

    await secondAcceptBtn.trigger('click')
    await secondRejectBtn.trigger('click')
    expect(wrapper.emitted('accept')?.[0]).toEqual(['capture-2', 'board-alpha'])
    expect(wrapper.emitted('reject')?.[0]).toEqual(['capture-2'])
  })

  // --- board picker write capability (#1836) -------------------------------

  async function openBoardPicker(boards: MockBoard[]) {
    mockBoardStore.boards = boards
    const items = makeItems()
    items[0] = { ...items[0], boardId: null }
    const wrapper = mount(PaperTriageTable, { props: { items } })
    await wrapper.findAll('button[data-action="accept"]')[0].trigger('click')
    return wrapper
  }

  it('renders a read-only board visible but disabled and annotated view-only', async () => {
    const wrapper = await openBoardPicker([
      { id: 'board-alpha', name: 'Alpha', canWrite: true },
      { id: 'board-readonly', name: 'Archive', canWrite: false },
    ])

    const options = wrapper.findAll('[data-testid="capture-board-pick"] option')
    const readOnly = options.find((option) => option.attributes('value') === 'board-readonly')

    // Visible, NOT filtered away.
    expect(readOnly).toBeDefined()
    expect(readOnly!.attributes('disabled')).toBeDefined()
    expect(readOnly!.text()).toContain('Archive')
    expect(readOnly!.text()).toContain('view-only')
    expect(wrapper.find('[data-testid="board-pick-view-only-hint"]').exists()).toBe(true)
  })

  it('leaves a write-capable board enabled and unannotated', async () => {
    const wrapper = await openBoardPicker([
      { id: 'board-alpha', name: 'Alpha', canWrite: true },
    ])

    const option = wrapper
      .findAll('[data-testid="capture-board-pick"] option')
      .find((o) => o.attributes('value') === 'board-alpha')

    expect(option!.attributes('disabled')).toBeUndefined()
    expect(option!.text()).toBe('Alpha')
    expect(option!.text()).not.toContain('view-only')
    // The hint is only shown when there IS something to explain.
    expect(wrapper.find('[data-testid="board-pick-view-only-hint"]').exists()).toBe(false)
  })

  it('treats a board with no canWrite field as writable (older payloads unchanged)', async () => {
    const wrapper = await openBoardPicker([{ id: 'board-alpha', name: 'Alpha' }])

    const option = wrapper
      .findAll('[data-testid="capture-board-pick"] option')
      .find((o) => o.attributes('value') === 'board-alpha')

    expect(option!.attributes('disabled')).toBeUndefined()
    expect(option!.text()).toBe('Alpha')
  })

  it('refuses to accept onto a board that turns read-only after it was picked', async () => {
    // Access can be revoked between the list load and the confirm click; the
    // picker must not emit an accept the server would answer with a 403.
    const wrapper = await openBoardPicker([
      { id: 'board-alpha', name: 'Alpha', canWrite: true },
    ])
    await wrapper.find('[data-testid="capture-board-pick"] select').setValue('board-alpha')

    mockBoardStore.boards = [{ id: 'board-alpha', name: 'Alpha', canWrite: false }]
    await wrapper.vm.$nextTick()

    const confirmBtn = wrapper.find('button[data-action="accept-on-board"]')
    expect(confirmBtn.attributes('disabled')).toBeDefined()
    await confirmBtn.trigger('click')
    expect(wrapper.emitted('accept')).toBeUndefined()
  })

  it('emits open when an item row excerpt is clicked', async () => {
    const wrapper = mount(PaperTriageTable, { props: { items: makeItems() } })
    const opener = wrapper.findAll('.paper-triage__open')[1]
    await opener.trigger('click')
    expect(wrapper.emitted('open')?.[0]).toEqual(['capture-2'])
  })
})
