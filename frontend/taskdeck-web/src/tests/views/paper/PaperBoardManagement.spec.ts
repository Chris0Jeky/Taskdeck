import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { computed, nextTick, reactive, ref } from 'vue'
import PaperBoardView from '../../../views/paper/PaperBoardView.vue'
import { useBoardKeyboardNav } from '../../../composables/useBoardKeyboardNav'
import type { BoardDetail, Card, Column } from '../../../types/board'
import type { ViewportMode } from '../../../composables/useViewportMode'

/**
 * Direct board management from the PAPER board surface (#1945 / ADR-0056).
 *
 * The gap this covers was never an API gap: `src/tests/api/*` covered the
 * clients and stayed green the whole time the canonical skin had no way to
 * reach them. So every test here drives the *rendered Paper DOM* — click the
 * button a user clicks — and asserts the `boardStore` action that a real user
 * action must reach. A test that called the handler directly would have passed
 * before this change too, and would prove nothing.
 */

const routerMock = { push: vi.fn() }
const routeMock = reactive({ params: { id: 'board-1' } })
const mockViewportMode = ref<ViewportMode>('desktop')

function makeColumn(partial: Partial<Column> = {}): Column {
  return {
    id: 'col-1',
    boardId: 'board-1',
    name: 'Backlog',
    position: 0,
    wipLimit: null,
    cardCount: 0,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    ...partial,
  }
}

function makeCard(id: string, columnId: string, title = 'card', position = 0): Card {
  return {
    id,
    boardId: 'board-1',
    columnId,
    title,
    description: '',
    dueDate: null,
    isBlocked: false,
    blockReason: null,
    position,
    labels: [],
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
  }
}

const columns: Column[] = [
  makeColumn({ id: 'col-backlog', name: 'Backlog', position: 0 }),
  makeColumn({ id: 'col-today', name: 'Today', position: 1 }),
  makeColumn({ id: 'col-done', name: 'Done', position: 2 }),
]

const board: BoardDetail = {
  id: 'board-1',
  name: 'Product Backlog',
  description: 'Primary board',
  isArchived: false,
  createdAt: new Date().toISOString(),
  updatedAt: new Date().toISOString(),
  columns,
}

/** Backlog holds a card; Today and Done are empty (so Today is deletable). */
const cardsByColumn = new Map<string, Card[]>([
  ['col-backlog', [makeCard('card-1', 'col-backlog', 'Ship it', 0)]],
  ['col-today', []],
  ['col-done', []],
])

const allCards = [...cardsByColumn.values()].flat()

const mockBoardStore = reactive({
  currentBoard: board as BoardDetail | null,
  currentBoardCards: allCards as Card[],
  cardsByColumn,
  currentBoardLabels: [],
  loading: false,
  error: null as string | null,
  fetchBoard: vi.fn(async () => {}),
  moveCard: vi.fn(async () => {}),
  createCard: vi.fn(async () => makeCard('card-new', 'col-backlog', 'new')),
  createColumn: vi.fn(async () => makeColumn()),
  updateColumn: vi.fn(async () => makeColumn()),
  deleteColumn: vi.fn(async () => {}),
  reorderColumns: vi.fn(async () => columns),
  updateBoard: vi.fn(async () => board),
  deleteBoard: vi.fn(async () => {}),
})

vi.mock('vue-router', () => ({
  useRoute: () => routeMock,
  useRouter: () => routerMock,
}))

vi.mock('../../../store/boardStore', () => ({
  useBoardStore: () => mockBoardStore,
}))

vi.mock('../../../composables/useViewportMode', () => ({
  useViewportMode: () => ({ mode: mockViewportMode }),
}))

function mountView(props: Record<string, unknown> = {}) {
  return mount(PaperBoardView, {
    attachTo: document.body,
    props,
    global: {
      stubs: {
        CardModal: {
          props: ['card', 'isOpen', 'labels'],
          template: '<div v-if="isOpen" data-testid="paper-card-modal">{{ card.title }}</div>',
        },
      },
    },
  })
}

/** The column section for a given id, as rendered on the page. */
function columnEl(id: string) {
  return document.querySelector(`[data-column-id="${id}"]`) as HTMLElement | null
}

beforeEach(() => {
  routerMock.push.mockClear()
  for (const value of Object.values(mockBoardStore)) {
    if (typeof value === 'function' && 'mockClear' in value) {
      ;(value as ReturnType<typeof vi.fn>).mockClear()
    }
  }
  mockBoardStore.currentBoard = board
  mockBoardStore.currentBoardCards = allCards
  mockBoardStore.cardsByColumn = cardsByColumn
  mockBoardStore.error = null
  mockBoardStore.loading = false
  mockViewportMode.value = 'desktop'
})

afterEach(() => {
  document.body.innerHTML = ''
})

describe('PaperBoardView — direct add-card', () => {
  it('offers a primary "+ card" per column and keeps "+ capture" as the secondary lane', () => {
    const wrapper = mountView()

    const addButtons = wrapper.findAll('[data-testid="paper-column-add-card"]')
    const captureButtons = wrapper.findAll('[data-testid="paper-column-capture"]')

    expect(addButtons).toHaveLength(3)
    expect(captureButtons).toHaveLength(3)
    expect(addButtons[0]?.text()).toContain('+ card')
    expect(captureButtons[0]?.text()).toContain('+ capture')

    // "Secondary" has to mean something checkable: `+ card` is the primary
    // button variant, `+ capture` is not a PaperHLBtn at all.
    expect(addButtons[0]?.attributes('data-variant')).toBe('primary')
    expect(captureButtons[0]?.attributes('data-variant')).toBeUndefined()
  })

  it('creates a card directly from the board without leaving it', async () => {
    const wrapper = mountView()

    const column = wrapper.findAll('[data-column-id]')[1]!
    await column.get('[data-testid="paper-column-add-card"]').trigger('click')
    await column.get('[data-action="add-card-input"]').setValue('  Write the ADR  ')
    await column.get('[data-testid="paper-card-composer"]').trigger('submit')
    await flushPromises()

    expect(mockBoardStore.createCard).toHaveBeenCalledTimes(1)
    expect(mockBoardStore.createCard).toHaveBeenCalledWith('board-1', {
      columnId: 'col-today',
      title: 'Write the ADR',
    })
    // Direct means direct: no navigation to Inbox, no proposal.
    expect(routerMock.push).not.toHaveBeenCalled()
    // A successful add closes the composer.
    expect(wrapper.find('[data-testid="paper-card-composer"]').exists()).toBe(false)
  })

  it('refuses a whitespace-only title instead of posting it', async () => {
    const wrapper = mountView()

    const column = wrapper.findAll('[data-column-id]')[1]!
    await column.get('[data-testid="paper-column-add-card"]').trigger('click')
    await column.get('[data-action="add-card-input"]').setValue('   ')

    expect(
      column.get('[data-testid="paper-card-composer-submit"]').attributes('disabled'),
    ).toBeDefined()

    await column.get('[data-testid="paper-card-composer"]').trigger('submit')
    await flushPromises()

    expect(mockBoardStore.createCard).not.toHaveBeenCalled()
  })

  it('keeps the board, the draft and the inline error when the create fails', async () => {
    // A stub that only rejects proves nothing here. The REAL store sets
    // `state.error` inside `handleApiError` and only then rethrows
    // (`store/board/cardStore.ts` createCard), and it was that store error —
    // not the rejection — that used to blank the board: the view's error
    // banner headed the same `v-if` chain as the lanes, so every lane and the
    // user's draft unmounted, and this very assertion could never have run.
    mockBoardStore.createCard.mockImplementationOnce(async () => {
      mockBoardStore.error = 'Failed to create card'
      throw new Error('boom')
    })
    const wrapper = mountView()

    await wrapper.findAll('[data-column-id]')[1]!
      .get('[data-testid="paper-column-add-card"]')
      .trigger('click')
    await wrapper.findAll('[data-column-id]')[1]!
      .get('[data-action="add-card-input"]')
      .setValue('Write the ADR')
    await wrapper.findAll('[data-column-id]')[1]!
      .get('[data-testid="paper-card-composer"]')
      .trigger('submit')
    await flushPromises()

    // The board survives the failure — it is what carries the draft.
    expect(wrapper.find('[data-testid="paper-board-lanes"]').exists()).toBe(true)
    expect(wrapper.findAll('[data-column-id]')).toHaveLength(3)

    // The inline error is reachable at all, and the draft is still there.
    expect(wrapper.get('[data-testid="paper-card-composer-error"]').text()).toContain(
      'Could not add the card',
    )
    expect(
      (wrapper.get('[data-action="add-card-input"]').element as HTMLTextAreaElement).value,
    ).toBe('Write the ADR')

    // The store error is still reported — above the board, not instead of it.
    expect(wrapper.get('.paper-board-view__error').text()).toBe('Failed to create card')
  })

  it('cancels the composer without creating anything', async () => {
    const wrapper = mountView()

    const column = wrapper.findAll('[data-column-id]')[0]!
    await column.get('[data-testid="paper-column-add-card"]').trigger('click')
    expect(wrapper.find('[data-testid="paper-card-composer"]').exists()).toBe(true)

    await column.get('[data-action="cancel-add-card"]').trigger('click')

    expect(wrapper.find('[data-testid="paper-card-composer"]').exists()).toBe(false)
    expect(mockBoardStore.createCard).not.toHaveBeenCalled()
  })

  it('satisfies the `n` shortcut DOM contract that useBoardKeyboardNav drives', async () => {
    // The `n` shortcut does not call a handler — it queries the DOM. This runs
    // the real composable against the real Paper markup, which is the only way
    // to prove the two halves still agree.
    mountView()

    const nav = useBoardKeyboardNav(computed(() => columns), () => 'board-1', computed(() => cardsByColumn))
    nav.createCardInSelectedColumn()
    await nextTick()

    const backlog = columnEl('col-backlog')
    expect(backlog?.querySelector('[data-action="add-card-input"]')).not.toBeNull()

    // The composable focuses the input on a macrotask; wait one out.
    await new Promise((resolve) => setTimeout(resolve, 0))
    expect(document.activeElement?.getAttribute('data-action')).toBe('add-card-input')
  })
})

describe('PaperBoardView — column settings', () => {
  it('opens a Paper column dialog seeded from the column', async () => {
    const wrapper = mountView()

    await wrapper.findAll('[data-testid="paper-column-edit"]')[1]!.trigger('click')

    expect(wrapper.find('[data-testid="paper-column-dialog"]').exists()).toBe(true)
    expect(
      (wrapper.get('[data-testid="paper-column-dialog-name"]').element as HTMLInputElement).value,
    ).toBe('Today')
  })

  it('renames a column through boardStore.updateColumn and closes', async () => {
    const wrapper = mountView()

    await wrapper.findAll('[data-testid="paper-column-edit"]')[1]!.trigger('click')
    await wrapper.get('[data-testid="paper-column-dialog-name"]').setValue('This Week')
    await wrapper.get('[data-testid="paper-column-dialog-save"]').trigger('click')
    await flushPromises()

    expect(mockBoardStore.updateColumn).toHaveBeenCalledWith('board-1', 'col-today', {
      name: 'This Week',
      wipLimit: null,
      position: null,
    })
    expect(wrapper.find('[data-testid="paper-column-dialog"]').exists()).toBe(false)
  })

  it('sets a WIP limit through the same update call', async () => {
    const wrapper = mountView()

    await wrapper.findAll('[data-testid="paper-column-edit"]')[1]!.trigger('click')
    await wrapper.get('[data-testid="paper-column-dialog-wip-toggle"]').setValue(true)
    await wrapper.get('[data-testid="paper-column-dialog-wip"]').setValue(3)
    await wrapper.get('[data-testid="paper-column-dialog-save"]').trigger('click')
    await flushPromises()

    expect(mockBoardStore.updateColumn).toHaveBeenCalledWith('board-1', 'col-today', {
      name: null,
      wipLimit: 3,
      position: null,
    })
  })

  it('deletes an empty column only after an explicit in-dialog confirm', async () => {
    const wrapper = mountView()

    await wrapper.findAll('[data-testid="paper-column-edit"]')[1]!.trigger('click')
    await wrapper.get('[data-testid="paper-column-dialog-delete"]').trigger('click')

    // The first click asks; it must not delete.
    expect(mockBoardStore.deleteColumn).not.toHaveBeenCalled()
    expect(wrapper.find('[data-testid="paper-column-dialog-delete-confirm"]').exists()).toBe(true)

    await wrapper.get('[data-testid="paper-column-dialog-delete-confirm-yes"]').trigger('click')
    await flushPromises()

    expect(mockBoardStore.deleteColumn).toHaveBeenCalledWith('board-1', 'col-today')
    expect(wrapper.find('[data-testid="paper-column-dialog"]').exists()).toBe(false)
  })

  it('backs out of the delete confirm without deleting', async () => {
    const wrapper = mountView()

    await wrapper.findAll('[data-testid="paper-column-edit"]')[1]!.trigger('click')
    await wrapper.get('[data-testid="paper-column-dialog-delete"]').trigger('click')
    await wrapper.get('[data-testid="paper-column-dialog-delete-confirm-no"]').trigger('click')

    expect(wrapper.find('[data-testid="paper-column-dialog-delete-confirm"]').exists()).toBe(false)
    expect(mockBoardStore.deleteColumn).not.toHaveBeenCalled()
  })

  it('refuses to delete a column that still holds cards, and says why', async () => {
    const wrapper = mountView()

    // Backlog holds card-1.
    await wrapper.findAll('[data-testid="paper-column-edit"]')[0]!.trigger('click')

    expect(
      wrapper.get('[data-testid="paper-column-dialog-delete"]').attributes('disabled'),
    ).toBeDefined()
    expect(wrapper.get('[data-testid="paper-column-dialog-delete-blocked"]').text()).toContain(
      'Move or delete the cards',
    )
  })

  it('closes the column dialog on Escape without navigating away', async () => {
    const wrapper = mountView()

    await wrapper.findAll('[data-testid="paper-column-edit"]')[1]!.trigger('click')
    expect(wrapper.find('[data-testid="paper-column-dialog"]').exists()).toBe(true)

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    await nextTick()

    expect(wrapper.find('[data-testid="paper-column-dialog"]').exists()).toBe(false)
    expect(routerMock.push).not.toHaveBeenCalled()
  })
})

describe('PaperBoardView — column reorder', () => {
  it('moves a column right through boardStore.reorderColumns', async () => {
    const wrapper = mountView()

    await wrapper.findAll('[data-testid="paper-column-move-right"]')[0]!.trigger('click')
    await flushPromises()

    expect(mockBoardStore.reorderColumns).toHaveBeenCalledWith('board-1', [
      'col-today',
      'col-backlog',
      'col-done',
    ])
  })

  it('moves a column left through boardStore.reorderColumns', async () => {
    const wrapper = mountView()

    await wrapper.findAll('[data-testid="paper-column-move-left"]')[2]!.trigger('click')
    await flushPromises()

    expect(mockBoardStore.reorderColumns).toHaveBeenCalledWith('board-1', [
      'col-backlog',
      'col-done',
      'col-today',
    ])
  })

  it('disables the reorder controls at each end of the board', () => {
    const wrapper = mountView()

    const left = wrapper.findAll('[data-testid="paper-column-move-left"]')
    const right = wrapper.findAll('[data-testid="paper-column-move-right"]')

    expect(left[0]?.attributes('disabled')).toBeDefined()
    expect(left[2]?.attributes('disabled')).toBeUndefined()
    expect(right[2]?.attributes('disabled')).toBeDefined()
    expect(right[0]?.attributes('disabled')).toBeUndefined()
  })
})

describe('PaperBoardView — add a column to a populated board', () => {
  it('creates a column from a board that already has columns', async () => {
    // The zero-column empty state is a first-run bootstrap and disappears the
    // moment a board has one lane. Before this control it was the only
    // add-column door, so a populated board was capped at its current lanes.
    const wrapper = mountView()

    expect(wrapper.find('[data-testid="paper-board-empty"]').exists()).toBe(false)
    await wrapper.get('[data-testid="paper-board-add-column"]').trigger('click')
    await wrapper.get('[data-testid="paper-board-add-column-name"]').setValue('  Review  ')
    await wrapper.get('[data-testid="paper-board-add-column-form"]').trigger('submit')
    await flushPromises()

    expect(mockBoardStore.createColumn).toHaveBeenCalledTimes(1)
    // Position omitted so the server appends — same call shape as Legacy's
    // toolbar form.
    expect(mockBoardStore.createColumn).toHaveBeenCalledWith('board-1', { name: 'Review' })
    // Direct means direct: no navigation, no proposal.
    expect(routerMock.push).not.toHaveBeenCalled()
    // A successful add collapses the form back to the button.
    expect(wrapper.find('[data-testid="paper-board-add-column-form"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="paper-board-add-column"]').exists()).toBe(true)
  })

  it('stays clearly secondary to the per-column "+ card" control', async () => {
    const wrapper = mountView()

    // "Secondary" has to mean something checkable: `+ card` is the primary
    // button variant, the lane-rail `+ column` is not.
    expect(
      wrapper.findAll('[data-testid="paper-column-add-card"]')[0]?.attributes('data-variant'),
    ).toBe('primary')
    expect(wrapper.get('[data-testid="paper-board-add-column"]').attributes('data-variant')).toBe(
      'default',
    )
  })

  it('refuses a whitespace-only column name instead of posting it', async () => {
    const wrapper = mountView()

    await wrapper.get('[data-testid="paper-board-add-column"]').trigger('click')
    await wrapper.get('[data-testid="paper-board-add-column-name"]').setValue('   ')

    expect(
      wrapper.get('[data-testid="paper-board-add-column-submit"]').attributes('disabled'),
    ).toBeDefined()

    await wrapper.get('[data-testid="paper-board-add-column-form"]').trigger('submit')
    await flushPromises()

    expect(mockBoardStore.createColumn).not.toHaveBeenCalled()
  })

  it('cancels the inline form on Escape without creating anything', async () => {
    const wrapper = mountView()

    await wrapper.get('[data-testid="paper-board-add-column"]').trigger('click')
    await wrapper.get('[data-testid="paper-board-add-column-name"]').setValue('Review')
    await wrapper.get('[data-testid="paper-board-add-column-name"]').trigger('keydown.esc')

    expect(wrapper.find('[data-testid="paper-board-add-column-form"]').exists()).toBe(false)
    expect(mockBoardStore.createColumn).not.toHaveBeenCalled()
    expect(routerMock.push).not.toHaveBeenCalled()
  })

  it('keeps the form and its draft when the create fails', async () => {
    mockBoardStore.createColumn.mockRejectedValueOnce(new Error('boom'))
    const wrapper = mountView()

    await wrapper.get('[data-testid="paper-board-add-column"]').trigger('click')
    await wrapper.get('[data-testid="paper-board-add-column-name"]').setValue('Review')
    await wrapper.get('[data-testid="paper-board-add-column-form"]').trigger('submit')
    await flushPromises()

    expect(wrapper.get('[data-testid="paper-board-add-column-error"]').text()).toContain(
      'Could not create the column',
    )
    expect(
      (wrapper.get('[data-testid="paper-board-add-column-name"]').element as HTMLInputElement)
        .value,
    ).toBe('Review')
  })
})

describe('PaperBoardView — board settings', () => {
  it('opens a Paper board dialog seeded from the board', async () => {
    const wrapper = mountView()

    await wrapper.get('[data-testid="paper-board-settings"]').trigger('click')

    expect(wrapper.find('[data-testid="paper-board-dialog"]').exists()).toBe(true)
    expect(
      (wrapper.get('[data-testid="paper-board-dialog-name"]').element as HTMLInputElement).value,
    ).toBe('Product Backlog')
    expect(wrapper.get('[data-testid="paper-board-dialog-state"]').text()).toBe('Active')
  })

  it('renames the board through boardStore.updateBoard and closes', async () => {
    const wrapper = mountView()

    await wrapper.get('[data-testid="paper-board-settings"]').trigger('click')
    await wrapper.get('[data-testid="paper-board-dialog-name"]').setValue('Roadmap')
    await wrapper.get('[data-testid="paper-board-dialog-save"]').trigger('click')
    await flushPromises()

    expect(mockBoardStore.updateBoard).toHaveBeenCalledWith('board-1', {
      name: 'Roadmap',
      description: null,
      isArchived: null,
    })
    expect(wrapper.find('[data-testid="paper-board-dialog"]').exists()).toBe(false)
  })

  it('archives the board only after an explicit confirm, and leaves the board first', async () => {
    const wrapper = mountView()

    await wrapper.get('[data-testid="paper-board-settings"]').trigger('click')
    await wrapper.get('[data-testid="paper-board-dialog-archive"]').trigger('click')

    expect(mockBoardStore.deleteBoard).not.toHaveBeenCalled()
    expect(wrapper.find('[data-testid="paper-board-dialog-archive-confirm"]').exists()).toBe(true)
    expect(wrapper.get('[data-testid="paper-board-dialog-archive-confirm"]').text()).toContain(
      'Captures and decision history stay saved.',
    )
    expect(wrapper.get('[data-testid="paper-board-dialog-archive-confirm"]').text()).toContain(
      'they will not appear in the unfiltered Inbox or Review while this board is archived.',
    )

    await wrapper.get('[data-testid="paper-board-dialog-archive-confirm-yes"]').trigger('click')
    await flushPromises()

    expect(routerMock.push).toHaveBeenCalledWith({ name: 'workspace-boards' })
    expect(mockBoardStore.deleteBoard).toHaveBeenCalledWith('board-1')
    // #519: navigate away BEFORE the store teardown, never after.
    expect(routerMock.push.mock.invocationCallOrder[0]).toBeLessThan(
      mockBoardStore.deleteBoard.mock.invocationCallOrder[0]!,
    )
  })

  it('offers Restore instead of Archive for an archived board', async () => {
    mockBoardStore.currentBoard = { ...board, isArchived: true }
    const wrapper = mountView()

    await wrapper.get('[data-testid="paper-board-settings"]').trigger('click')

    expect(wrapper.find('[data-testid="paper-board-dialog-archive"]').exists()).toBe(false)
    expect(wrapper.get('[data-testid="paper-board-dialog-state"]').text()).toBe('Archived')

    await wrapper.get('[data-testid="paper-board-dialog-restore"]').trigger('click')
    await flushPromises()

    expect(mockBoardStore.updateBoard).toHaveBeenCalledWith('board-1', { isArchived: false })
  })

  it('never offers a permanent board delete, because the API has none', async () => {
    const wrapper = mountView()

    await wrapper.get('[data-testid="paper-board-settings"]').trigger('click')
    const dialog = wrapper.get('[data-testid="paper-board-dialog"]')

    expect(dialog.text()).toContain('Move to archive')
    // No CONTROL may promise destruction the server does not perform. The
    // lifecycle hint is allowed to use the word ("Nothing is deleted"); a
    // button is not.
    const controlLabels = dialog.findAll('button').map((node) => node.text())
    expect(controlLabels.filter((label) => /delete/i.test(label))).toEqual([])
  })
})

describe('PaperBoardView — dialogs and the board shortcuts', () => {
  it('reports dialog state up so the shortcut owner can gate on it', async () => {
    const wrapper = mountView()

    expect(wrapper.emitted('dialog-open-change')).toBeUndefined()

    await wrapper.get('[data-testid="paper-board-settings"]').trigger('click')
    expect(wrapper.emitted('dialog-open-change')?.at(-1)).toEqual([true])

    await wrapper.get('[data-action="close-dialog"]').trigger('click')
    expect(wrapper.emitted('dialog-open-change')?.at(-1)).toEqual([false])
  })

  it('reports the column dialog and the card modal too', async () => {
    const wrapper = mountView()

    await wrapper.findAll('[data-testid="paper-column-edit"]')[1]!.trigger('click')
    expect(wrapper.emitted('dialog-open-change')?.at(-1)).toEqual([true])

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    await nextTick()
    expect(wrapper.emitted('dialog-open-change')?.at(-1)).toEqual([false])

    await wrapper.get('[data-action="open-card"]').trigger('click')
    expect(wrapper.find('[data-testid="paper-card-modal"]').exists()).toBe(true)
    expect(wrapper.emitted('dialog-open-change')?.at(-1)).toEqual([true])
  })

  it('clears the flag if the view unmounts with a dialog still open', async () => {
    const wrapper = mountView()

    await wrapper.get('[data-testid="paper-board-settings"]').trigger('click')
    expect(wrapper.emitted('dialog-open-change')?.at(-1)).toEqual([true])

    // A skin switch destroys this view. A flag stuck at `true` would leave the
    // Legacy board's shortcuts dead.
    wrapper.unmount()
    expect(wrapper.emitted('dialog-open-change')?.at(-1)).toEqual([false])
  })

  it('moves focus into the dialog on open and hands it back on close', async () => {
    const wrapper = mountView()

    const opener = wrapper.get('[data-testid="paper-board-settings"]').element as HTMLElement
    opener.focus()
    expect(document.activeElement).toBe(opener)

    await wrapper.get('[data-testid="paper-board-settings"]').trigger('click')
    await nextTick()

    const panel = document.querySelector('.paper-board-dialog')
    expect(panel).not.toBeNull()
    expect(document.activeElement).toBe(panel)

    await wrapper.get('[data-action="close-dialog"]').trigger('click')
    await nextTick()

    expect(document.activeElement).toBe(opener)
  })
})

describe('PaperBoardView — the capture lane still exists', () => {
  it('routes "+ capture" to the column-scoped Inbox composer', async () => {
    const wrapper = mountView()

    await wrapper.findAll('[data-testid="paper-column-capture"]')[1]!.trigger('click')

    expect(routerMock.push).toHaveBeenCalledWith({
      name: 'workspace-inbox',
      query: { boardId: 'board-1', columnId: 'col-today' },
    })
  })
})
