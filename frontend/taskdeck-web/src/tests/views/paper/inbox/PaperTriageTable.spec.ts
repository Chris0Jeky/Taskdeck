import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
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

/**
 * The inline row editor (GH-1951) reads and writes through the capture store.
 * Its own behaviour is covered in `PaperTriageRowEdit.spec.ts`; here the store
 * is stubbed only so mounting the table can never reach the network.
 */
const mockCaptureStore = {
  fetchDetail: vi.fn<(...args: unknown[]) => Promise<unknown>>(),
  updateSuggestion: vi.fn<(...args: unknown[]) => Promise<unknown>>(),
}

vi.mock('../../../../store/captureStore', () => ({
  useCaptureStore: () => mockCaptureStore,
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
    mockCaptureStore.fetchDetail.mockResolvedValue({
      id: 'capture-1',
      userId: 'user-1',
      boardId: 'board-alpha',
      status: 'New',
      source: 'Typed',
      textExcerpt: 'First excerpt',
      rawText: 'First excerpt in full',
      createdAt: new Date('2026-04-25T09:42:00Z').toISOString(),
      processedAt: null,
      retryCount: 0,
      provenance: null,
      canEditSuggestion: true,
    })
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

  it('hides retained rows and their count while a replacement list is loading', () => {
    const wrapper = mount(PaperTriageTable, {
      props: { items: makeItems(), loadingList: true },
    })

    expect(wrapper.get('.paper-triage').attributes('aria-busy')).toBe('true')
    expect(wrapper.get('.paper-triage__empty[role="status"]').text()).toContain('Loading')
    expect(wrapper.get('.paper-triage__list').attributes('style')).toContain('display: none')
    expect(wrapper.findAll('.paper-triage__row')).toHaveLength(2)
    expect(wrapper.text()).not.toContain('2 items')
  })

  it('prioritizes an error and hides retained rows and their count after replacement fails', () => {
    const wrapper = mount(PaperTriageTable, {
      props: { items: makeItems(), loadingList: true, listError: 'Refresh failed' },
    })

    expect(wrapper.find('[role="alert"]').text()).toContain('Refresh failed')
    expect(wrapper.find('.paper-triage__empty[role="status"]').exists()).toBe(false)
    expect(wrapper.get('.paper-triage__list').attributes('style')).toContain('display: none')
    expect(wrapper.findAll('.paper-triage__row')).toHaveLength(2)
    expect(wrapper.text()).not.toContain('2 items')
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

  // --- blocked primary actions state their reason (#1944) -------------------
  //
  // The reported defect: "Accept on board" with nothing selected fired zero
  // network requests and said nothing. Asserting "no request is issued" would
  // pin the BROKEN behaviour, so these assert the guard instead — the button is
  // off, the row says why, and the button points at that reason.

  it('blocks "Accept on board" with a visible reason when no board is selected', async () => {
    const items = makeItems()
    items[0] = { ...items[0], boardId: null }
    const wrapper = mount(PaperTriageTable, { props: { items } })
    await wrapper.findAll('button[data-action="accept"]')[0].trigger('click')

    const confirmBtn = wrapper.find('button[data-action="accept-on-board"]')
    const reason = wrapper.find('[data-testid="board-pick-reason"]')

    expect(confirmBtn.attributes('disabled')).toBeDefined()
    expect(reason.exists()).toBe(true)
    expect(reason.attributes('data-reason')).toBe('noBoard')
    expect(reason.text()).toContain('Choose a board first')
    // The reason is wired to the button, not merely adjacent to it.
    expect(confirmBtn.attributes('aria-describedby')).toBe(reason.attributes('id'))

    await confirmBtn.trigger('click')
    expect(wrapper.emitted('accept')).toBeUndefined()
  })

  it('clears the blocked reason and enables the confirm once a writable board is chosen', async () => {
    const items = makeItems()
    items[0] = { ...items[0], boardId: null }
    const wrapper = mount(PaperTriageTable, { props: { items } })
    await wrapper.findAll('button[data-action="accept"]')[0].trigger('click')
    await wrapper.find('[data-testid="capture-board-pick"] select').setValue('board-beta')

    const confirmBtn = wrapper.find('button[data-action="accept-on-board"]')
    expect(wrapper.find('[data-testid="board-pick-reason"]').exists()).toBe(false)
    expect(confirmBtn.attributes('disabled')).toBeUndefined()
    expect(confirmBtn.attributes('aria-describedby')).toBeUndefined()

    await confirmBtn.trigger('click')
    expect(wrapper.emitted('accept')?.[0]).toEqual(['capture-1', 'board-beta'])
  })

  it('states the reason when the picked board turns read-only', async () => {
    const wrapper = await openBoardPicker([{ id: 'board-alpha', name: 'Alpha', canWrite: true }])
    await wrapper.find('[data-testid="capture-board-pick"] select').setValue('board-alpha')

    mockBoardStore.boards = [{ id: 'board-alpha', name: 'Alpha', canWrite: false }]
    await wrapper.vm.$nextTick()

    const reason = wrapper.find('[data-testid="board-pick-reason"]')
    expect(reason.attributes('data-reason')).toBe('viewOnly')
    expect(reason.text()).toContain('view-only')
    expect(wrapper.find('button[data-action="accept-on-board"]').attributes('disabled')).toBeDefined()
  })

  it('states that boards are loading without claiming the account is empty', async () => {
    mockBoardStore.boards = []
    let resolveFetch!: () => void
    mockBoardStore.fetchBoards.mockImplementationOnce(
      () => new Promise<void>((resolve) => { resolveFetch = resolve }),
    )
    const items = makeItems()
    items[0] = { ...items[0], boardId: null }
    const wrapper = mount(PaperTriageTable, { props: { items } })

    await wrapper.findAll('button[data-action="accept"]')[0].trigger('click')

    const picker = wrapper.get('[data-testid="capture-board-pick"]')
    const reason = wrapper.get('[data-testid="board-pick-reason"]')
    expect(picker.attributes('aria-busy')).toBe('true')
    expect(reason.attributes('data-reason')).toBe('loading')
    expect(reason.attributes('role')).toBe('status')
    expect(reason.text()).toBe('Loading boards…')
    expect(wrapper.text()).not.toContain('No boards yet')
    expect(picker.get('select').attributes('disabled')).toBeDefined()
    expect(wrapper.get('button[data-action="accept-on-board"]').attributes('disabled')).toBeDefined()
    expect(wrapper.find('button[data-action="retry-board-load"]').exists()).toBe(false)

    resolveFetch()
    await flushPromises()
  })

  it('states a board-load failure and offers an accessible retry', async () => {
    mockBoardStore.boards = []
    mockBoardStore.fetchBoards.mockRejectedValueOnce(new Error('Network unavailable'))
    const items = makeItems()
    items[0] = { ...items[0], boardId: null }
    const wrapper = mount(PaperTriageTable, { props: { items } })

    await flushPromises()
    await wrapper.findAll('button[data-action="accept"]')[0].trigger('click')

    const reason = wrapper.get('[data-testid="board-pick-reason"]')
    const retry = wrapper.get('button[data-action="retry-board-load"]')
    expect(reason.attributes('data-reason')).toBe('loadFailed')
    expect(reason.attributes('role')).toBe('alert')
    expect(reason.text()).toContain('Boards could not be loaded')
    expect(wrapper.text()).not.toContain('No boards yet')
    expect(retry.text()).toBe('Retry board load')
    expect(retry.attributes('type')).toBe('button')
    expect(retry.attributes('aria-describedby')).toBe(reason.attributes('id'))
    expect(wrapper.get('button[data-action="accept-on-board"]').attributes('disabled')).toBeDefined()
    expect(mockBoardStore.fetchBoards).toHaveBeenCalledTimes(1)
  })

  it('shows the create-a-board state only after a successful empty response', async () => {
    mockBoardStore.boards = []
    const items = makeItems()
    items[0] = { ...items[0], boardId: null }
    const wrapper = mount(PaperTriageTable, { props: { items } })

    await flushPromises()
    await wrapper.findAll('button[data-action="accept"]')[0].trigger('click')

    const picker = wrapper.get('[data-testid="capture-board-pick"]')
    const reason = wrapper.get('[data-testid="board-pick-reason"]')
    expect(picker.attributes('aria-busy')).toBe('false')
    expect(reason.attributes('data-reason')).toBe('noBoards')
    expect(reason.text()).toContain('No boards yet')
    expect(wrapper.get('button[data-action="accept-on-board"]').attributes('disabled')).toBeDefined()
    expect(wrapper.find('button[data-action="retry-board-load"]').exists()).toBe(false)
    expect(mockBoardStore.fetchBoards).toHaveBeenCalledTimes(1)
  })

  it('moves deterministically from failure through retry to loaded boards', async () => {
    mockBoardStore.boards = []
    let resolveRetry!: () => void
    mockBoardStore.fetchBoards
      .mockRejectedValueOnce(new Error('Network unavailable'))
      .mockImplementationOnce(
        () => new Promise<void>((resolve) => {
          resolveRetry = () => {
            mockBoardStore.boards = [{ id: 'board-recovered', name: 'Recovered' }]
            resolve()
          }
        }),
      )
    const items = makeItems()
    items[0] = { ...items[0], boardId: null }
    const wrapper = mount(PaperTriageTable, { props: { items } })

    await flushPromises()
    await wrapper.findAll('button[data-action="accept"]')[0].trigger('click')
    expect(wrapper.get('[data-testid="board-pick-reason"]').attributes('data-reason')).toBe('loadFailed')

    await wrapper.get('button[data-action="retry-board-load"]').trigger('click')
    expect(wrapper.get('[data-testid="board-pick-reason"]').attributes('data-reason')).toBe('loading')

    resolveRetry()
    await flushPromises()

    const picker = wrapper.get('[data-testid="capture-board-pick"]')
    expect(picker.attributes('aria-busy')).toBe('false')
    expect(wrapper.find('button[data-action="retry-board-load"]').exists()).toBe(false)
    expect(wrapper.get('[data-testid="board-pick-reason"]').attributes('data-reason')).toBe('noBoard')
    expect(picker.get('select').attributes('disabled')).toBeUndefined()
    expect(picker.get('select').text()).toContain('Recovered')
    expect(mockBoardStore.fetchBoards).toHaveBeenCalledTimes(2)
  })

  // --- a decided row never looks like an undecided one (#1944) --------------

  const undecidedRow = () => {
    const items = makeItems()
    items[0] = { ...items[0], status: 'New' }
    return mount(PaperTriageTable, { props: { items } }).find('.paper-triage__row')
  }

  it('says nothing about a decision while the row is still awaiting one', () => {
    const row = undecidedRow()
    expect(row.attributes('data-row-state')).toBe('undecided')
    expect(row.find('[data-testid="capture-row-status"]').exists()).toBe(false)
  })

  it.each<[CaptureStatusValue, string, string]>([
    ['Triaging', 'sending', 'Sending to Review'],
    ['Triaged', 'nothingToPropose', 'nothing to propose'],
    ['ProposalCreated', 'inReview', 'Sent to Review'],
    ['Converted', 'applied', 'Applied to the board'],
    ['Ignored', 'rejected', 'Rejected'],
    ['Failed', 'failed', 'nothing reached Review'],
  ])('pins the post-decision state for %s', (status, expectedState, expectedCopy) => {
    const items = makeItems()
    items[0] = { ...items[0], status }
    const wrapper = mount(PaperTriageTable, { props: { items } })
    const row = wrapper.find('.paper-triage__row')
    const line = row.find('[data-testid="capture-row-status"]')

    expect(row.attributes('data-row-state')).toBe(expectedState)
    expect(line.exists()).toBe(true)
    expect(line.text()).toContain(expectedCopy)
    // The load-bearing invariant: a decided row cannot render like an
    // undecided one, in state OR in what the user reads.
    expect(row.attributes('data-row-state')).not.toBe(undecidedRow().attributes('data-row-state'))
    expect(row.text()).not.toBe(undecidedRow().text())
  })

  it('never tells a "nothing to propose" row to go decide in Review', () => {
    // A triage that completed with no proposal is a SUCCESS with nothing left
    // to decide (backend: CaptureStatusPolicy maps completed-without-proposal
    // to Triaged). Accept and Reject are both disabled on this row and polling
    // has stopped, so "decide there" would be a permanent instruction the user
    // has no way to act on.
    const items = makeItems()
    items[0] = { ...items[0], status: 'Triaged' }
    const wrapper = mount(PaperTriageTable, { props: { items } })
    const line = wrapper.find('.paper-triage__row [data-testid="capture-row-status"]')

    expect(line.text()).toContain('nothing to propose')
    expect(line.text()).not.toContain('decide there')
  })

  // In-flight narration must carry the intent the user actually clicked. The
  // `actionBusyItemId` prop cannot: captureStore sets the same single slot for
  // `triageItem` (Accept) and `ignoreItem` (Reject) alike.

  it('reports a row as sending the moment its own accept is in flight', async () => {
    // Feedback must not wait for the next status poll to arrive.
    const items = makeItems()
    items[0] = { ...items[0], status: 'New' }
    const wrapper = mount(PaperTriageTable, {
      props: { items, actionBusyItemId: null },
    })

    await wrapper.findAll('button[data-action="accept"]')[0].trigger('click')
    await wrapper.setProps({ actionBusyItemId: 'capture-1' })
    const row = wrapper.find('.paper-triage__row')

    expect(row.attributes('data-row-state')).toBe('sending')
    expect(row.find('[data-testid="capture-row-status"]').text()).toContain('Sending to Review')
  })

  it('narrates a rejection, not a trip to Review, while its own reject is in flight', async () => {
    const items = makeItems()
    items[0] = { ...items[0], status: 'New' }
    const wrapper = mount(PaperTriageTable, {
      props: { items, actionBusyItemId: null },
    })

    await wrapper.findAll('button[data-action="reject"]')[0].trigger('click')
    await wrapper.setProps({ actionBusyItemId: 'capture-1' })
    const row = wrapper.find('.paper-triage__row')
    const line = row.find('[data-testid="capture-row-status"]')

    expect(row.attributes('data-row-state')).toBe('rejecting')
    expect(line.text()).toContain('Rejecting')
    expect(line.text()).not.toContain('Sending to Review')
  })

  it('falls back to the server status for a busy row this table did not act on', async () => {
    // The busy slot is shared: another surface (the detail panel) can set it
    // for a row nobody clicked here. With no intent of our own, the honest
    // answer is the status — never an invented "Sending to Review…".
    const items = makeItems()
    items[0] = { ...items[0], status: 'New' }
    const wrapper = mount(PaperTriageTable, {
      props: { items, actionBusyItemId: 'capture-1' },
    })
    const row = wrapper.find('.paper-triage__row')

    expect(row.attributes('data-row-state')).toBe('undecided')
    expect(row.find('[data-testid="capture-row-status"]').exists()).toBe(false)
  })

  it('does not narrate a decision for an out-of-contract status', () => {
    const items = makeItems()
    items[0] = { ...items[0], status: 'Quarantined' as unknown as CaptureStatusValue }
    const wrapper = mount(PaperTriageTable, { props: { items } })
    const row = wrapper.find('.paper-triage__row')

    expect(row.attributes('data-row-state')).toBe('unknown')
    expect(row.find('[data-testid="capture-row-status"]').exists()).toBe(false)
  })

  // --- source tags are not state tags (#1944) -------------------------------

  it('marks and explains the source tag separately from the state tag', () => {
    const wrapper = mount(PaperTriageTable, { props: { items: makeItems() } })
    const stateTag = wrapper.find('[data-tag-kind="state"]')
    const sourceTag = wrapper.find('[data-tag-kind="source"]')

    expect(stateTag.text()).toBe('New')
    expect(stateTag.attributes('title')).toContain('State: New')
    expect(sourceTag.text()).toBe('Typed')
    expect(sourceTag.attributes('title')).toContain('Source: Typed')
    expect(sourceTag.attributes('title')).toContain('not a state')
    expect(sourceTag.classes()).toContain('paper-triage__tag--source')
  })

  it('emits open when an item row excerpt is clicked', async () => {
    const wrapper = mount(PaperTriageTable, { props: { items: makeItems() } })
    const opener = wrapper.findAll('.paper-triage__open')[1]
    await opener.trigger('click')
    expect(wrapper.emitted('open')?.[0]).toEqual(['capture-2'])
  })

  // --- pre-triage text edit (GH-1951) ---------------------------------------

  it('opens the inline editor from a pre-triage row', async () => {
    const wrapper = mount(PaperTriageTable, { props: { items: makeItems() } })
    expect(wrapper.find('[data-testid="capture-edit"]').exists()).toBe(false)

    await wrapper.findAll('button[data-action="edit"]')[0].trigger('click')
    await flushPromises()

    const rows = wrapper.findAll('.paper-triage__row')
    expect(rows[0].find('[data-testid="capture-edit"]').exists()).toBe(true)
    // One row at a time: a second open editor would give the user two drafts.
    expect(rows[1].find('[data-testid="capture-edit"]').exists()).toBe(false)
    expect(mockCaptureStore.fetchDetail).toHaveBeenCalledWith(
      'capture-1',
      expect.objectContaining({ forceRefresh: true }),
    )
  })

  it('preserves an unsaved edit while retained rows are hidden during a refresh', async () => {
    const wrapper = mount(PaperTriageTable, { props: { items: makeItems() } })
    await wrapper.findAll('button[data-action="edit"]')[0].trigger('click')
    await flushPromises()

    const editorBeforeRefresh = wrapper.get('[data-testid="capture-edit"]').element
    const typed = 'a correction that has not been saved yet'
    await wrapper.get('[data-testid="capture-edit-textarea"]').setValue(typed)

    await wrapper.setProps({ loadingList: true })
    await flushPromises()

    expect(wrapper.get('.paper-triage__list').attributes('style')).toContain('display: none')
    expect(wrapper.get('[data-testid="capture-edit"]').element).toBe(editorBeforeRefresh)
    expect(wrapper.get<HTMLTextAreaElement>('[data-testid="capture-edit-textarea"]').element.value)
      .toBe(typed)
    expect(mockCaptureStore.fetchDetail).toHaveBeenCalledTimes(1)

    await wrapper.setProps({ loadingList: false })
    await flushPromises()

    expect(wrapper.get('.paper-triage__list').attributes('style')).toBeUndefined()
    expect(wrapper.get<HTMLTextAreaElement>('[data-testid="capture-edit-textarea"]').element.value)
      .toBe(typed)
    expect(mockCaptureStore.fetchDetail).toHaveBeenCalledTimes(1)
  })

  it('offers the editor on a failed row, which is still pre-triage', async () => {
    const items = makeItems()
    items[1] = { ...items[1], status: 'Failed' }
    const wrapper = mount(PaperTriageTable, { props: { items } })

    expect(wrapper.findAll('button[data-action="edit"]')[1].attributes('disabled')).toBeUndefined()
  })

  it('lets a failed row correct stranded metadata and then retry Accept', async () => {
    const items = makeItems()
    items[0] = {
      ...items[0],
      status: 'Failed',
      errorMessage: "Label 'shoping' was not found on the proposal board",
    }
    mockCaptureStore.fetchDetail.mockResolvedValue({
      ...items[0],
      rawText: 'First excerpt in full',
      retryCount: 1,
      provenance: null,
      canEditSuggestion: true,
      metadata: {
        dueDate: null,
        labels: ['shoping'],
      },
    })
    const wrapper = mount(PaperTriageTable, { props: { items } })

    await wrapper.findAll('button[data-action="edit"]')[0].trigger('click')
    await flushPromises()
    await wrapper.get('button[data-action="remove-label"]').trigger('click')
    const labelInput = wrapper.get('[data-testid="capture-edit-label-input"]')
    await labelInput.setValue('shopping')
    await labelInput.trigger('keydown', { key: 'Enter' })
    await wrapper.get('button[data-action="edit-save"]').trigger('click')
    await flushPromises()

    expect(mockCaptureStore.updateSuggestion).toHaveBeenCalledWith('capture-1', {
      text: 'First excerpt in full',
      metadata: {
        dueDate: null,
        labels: ['shopping'],
      },
    })
    expect(wrapper.find('[data-testid="capture-edit"]').exists()).toBe(false)

    await wrapper.findAll('button[data-action="accept"]')[0].trigger('click')
    expect(wrapper.emitted('accept')?.at(-1)).toEqual(['capture-1', 'board-alpha'])
  })

  it('does not offer the editor on a row that can no longer be mutated', async () => {
    // `capture-2` is Triaging: its text is already on its way through triage,
    // so an edit here would be a promise the server would refuse.
    const wrapper = mount(PaperTriageTable, { props: { items: makeItems() } })
    const editBtn = wrapper.findAll('button[data-action="edit"]')[1]

    expect(editBtn.attributes('disabled')).toBeDefined()

    // The guard behind the disabled button, not just the binding.
    await editBtn.trigger('click')
    await flushPromises()
    expect(wrapper.findAll('.paper-triage__row')[1].find('[data-testid="capture-edit"]').exists()).toBe(false)
    expect(mockCaptureStore.fetchDetail).not.toHaveBeenCalled()
  })

  it('holds Accept and Reject shut while an unsaved edit is open, and says why', async () => {
    const wrapper = mount(PaperTriageTable, { props: { items: makeItems() } })
    await wrapper.findAll('button[data-action="edit"]')[0].trigger('click')
    await flushPromises()

    const row = wrapper.findAll('.paper-triage__row')[0]
    // Accepting now would triage the text the user is halfway through replacing
    // and drop the correction without a word.
    expect(row.find('button[data-action="accept"]').attributes('disabled')).toBeDefined()
    expect(row.find('button[data-action="reject"]').attributes('disabled')).toBeDefined()
    expect(row.find('[data-testid="capture-edit-decision-block"]').text())
      .toContain('Finish or cancel this edit')

    await row.find('button[data-action="accept"]').trigger('click')
    expect(wrapper.emitted('accept')).toBeUndefined()
  })

  it('gives Accept and Reject back when the editor closes', async () => {
    const wrapper = mount(PaperTriageTable, { props: { items: makeItems() } })
    await wrapper.findAll('button[data-action="edit"]')[0].trigger('click')
    await flushPromises()

    await wrapper.find('button[data-action="edit-cancel"]').trigger('click')
    await flushPromises()

    const row = wrapper.findAll('.paper-triage__row')[0]
    expect(row.find('[data-testid="capture-edit"]').exists()).toBe(false)
    expect(row.find('button[data-action="accept"]').attributes('disabled')).toBeUndefined()
  })

  it('does not narrate an open editor as a decision', async () => {
    // The row is still undecided while its text is being corrected — claiming
    // "Sending to Review…" here is the GH-1944 lie in a new place.
    const wrapper = mount(PaperTriageTable, { props: { items: makeItems() } })
    await wrapper.findAll('button[data-action="edit"]')[0].trigger('click')
    await flushPromises()

    const row = wrapper.findAll('.paper-triage__row')[0]
    expect(row.attributes('data-row-state')).toBe('undecided')
    expect(row.find('[data-testid="capture-row-status"]').exists()).toBe(false)
  })

  it('cancels a board pick in progress when an editor opens elsewhere', async () => {
    const items = makeItems()
    items[0] = { ...items[0], boardId: null }
    items[1] = { ...items[1], status: 'New' }
    const wrapper = mount(PaperTriageTable, { props: { items } })

    // Row 0 is board-less, so Accept reveals its picker. While a row is picking
    // it renders the picker INSTEAD of its action buttons, so the only Edit
    // button left on the surface belongs to row 1.
    await wrapper.findAll('button[data-action="accept"]')[0].trigger('click')
    await flushPromises()
    expect(wrapper.find('[data-testid="capture-board-pick"]').exists()).toBe(true)

    await wrapper.get('button[data-action="edit"]').trigger('click')
    await flushPromises()

    // `boardPickItemId` is a single slot: a picker left open on another row
    // would sit there confirmable against a decision the user has moved on from.
    expect(wrapper.find('[data-testid="capture-board-pick"]').exists()).toBe(false)
    expect(wrapper.findAll('.paper-triage__row')[1].find('[data-testid="capture-edit"]').exists()).toBe(true)
  })

  it('refuses to switch the editor to another row and says the draft is why', async () => {
    // `editItemId` is one slot. Edit on row 1 would move it, unmounting row 0's
    // editor and taking the typed draft with it — silently, with no undo.
    //
    // Row 1 is forced to `New`: the fixture's `Triaging` row is already off for
    // an unrelated reason, so it could not tell this gate from its absence.
    const items = makeItems()
    items[1] = { ...items[1], status: 'New' }
    const wrapper = mount(PaperTriageTable, { props: { items } })
    await wrapper.findAll('button[data-action="edit"]')[0].trigger('click')
    await flushPromises()

    const typed = 'a correction the user is halfway through'
    await wrapper.get('[data-testid="capture-edit-textarea"]').setValue(typed)

    const siblingEdit = wrapper.findAll('button[data-action="edit"]')[1]
    expect(siblingEdit.attributes('disabled')).toBeDefined()

    const rows = wrapper.findAll('.paper-triage__row')
    const reason = rows[1].get('[data-testid="capture-editor-open-block"]')
    expect(reason.text()).toContain('Another capture is open for editing')
    // Off-and-silent is the failure this surface was reported for (GH-1944).
    expect(siblingEdit.attributes('aria-describedby')).toBe(reason.attributes('id'))

    // The guard behind the disabled button, not just the binding.
    await siblingEdit.trigger('click')
    await flushPromises()

    expect(rows[1].find('[data-testid="capture-edit"]').exists()).toBe(false)
    expect(wrapper.get<HTMLTextAreaElement>('[data-testid="capture-edit-textarea"]').element.value)
      .toBe(typed)
  })

  it('freezes a sibling row\'s Accept and Reject while an editor is open', async () => {
    // Accepting row 1 takes the shared busy slot and refreshes the list under
    // the open editor; the draft on row 0 has no claim on either.
    const items = makeItems()
    items[1] = { ...items[1], status: 'New' }
    const wrapper = mount(PaperTriageTable, { props: { items } })
    await wrapper.findAll('button[data-action="edit"]')[0].trigger('click')
    await flushPromises()

    const row = wrapper.findAll('.paper-triage__row')[1]
    expect(row.get('button[data-action="accept"]').attributes('disabled')).toBeDefined()
    expect(row.get('button[data-action="reject"]').attributes('disabled')).toBeDefined()

    await row.get('button[data-action="accept"]').trigger('click')
    await row.get('button[data-action="reject"]').trigger('click')
    expect(wrapper.emitted('accept')).toBeUndefined()
    expect(wrapper.emitted('reject')).toBeUndefined()
  })

  it('gives the sibling rows back the moment the editor closes', async () => {
    const items = makeItems()
    items[1] = { ...items[1], status: 'New' }
    const wrapper = mount(PaperTriageTable, { props: { items } })
    await wrapper.findAll('button[data-action="edit"]')[0].trigger('click')
    await flushPromises()

    await wrapper.get('button[data-action="edit-cancel"]').trigger('click')
    await flushPromises()

    const row = wrapper.findAll('.paper-triage__row')[1]
    expect(row.find('[data-testid="capture-editor-open-block"]').exists()).toBe(false)
    expect(row.get('button[data-action="edit"]').attributes('disabled')).toBeUndefined()
    expect(row.get('button[data-action="accept"]').attributes('disabled')).toBeUndefined()
  })

  it('hands the editor the shared busy slot so its Save respects it', async () => {
    // `updateSuggestion` writes `actionBusyItemId` the way Accept and Reject do,
    // and the editor cannot see that slot on its own.
    const wrapper = mount(PaperTriageTable, { props: { items: makeItems() } })
    // Edit is shut while a mutation is in flight, so the editor is opened first
    // and the slot taken afterwards — the order this hazard actually occurs in.
    await wrapper.findAll('button[data-action="edit"]')[0].trigger('click')
    await flushPromises()
    // A draft that WOULD save, so the only thing holding Save is the slot.
    await wrapper.get('[data-testid="capture-edit-textarea"]').setValue('corrected text')
    expect(wrapper.get('button[data-action="edit-save"]').attributes('disabled')).toBeUndefined()

    await wrapper.setProps({ actionBusyItemId: 'capture-2' })
    await flushPromises()

    const reason = wrapper.get('[data-testid="capture-edit-save-reason"]')
    expect(reason.attributes('data-reason')).toBe('busyElsewhere')
    expect(wrapper.get('button[data-action="edit-save"]').attributes('disabled')).toBeDefined()
  })

  it('closes a stale editor when its row leaves the list', async () => {
    const wrapper = mount(PaperTriageTable, { props: { items: makeItems() } })
    await wrapper.findAll('button[data-action="edit"]')[0].trigger('click')
    await flushPromises()
    expect(wrapper.find('[data-testid="capture-edit"]').exists()).toBe(true)

    await wrapper.setProps({ items: makeItems().slice(1) })
    await flushPromises()

    expect(wrapper.find('[data-testid="capture-edit"]').exists()).toBe(false)
  })
})
