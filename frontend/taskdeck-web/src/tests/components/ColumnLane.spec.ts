import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import ColumnLane from '../../components/board/ColumnLane.vue'
import { useBoardStore } from '../../store/boardStore'
import { useToastStore } from '../../store/toastStore'
import type { Column, Card, Label } from '../../types/board'

vi.mock('../../store/boardStore', () => ({
  useBoardStore: vi.fn(),
}))

function makeColumn(overrides: Partial<Column> = {}): Column {
  return {
    id: 'col-1',
    boardId: 'board-1',
    name: 'In Progress',
    position: 0,
    wipLimit: null,
    cardCount: 0,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    ...overrides,
  }
}

function makeCard(id: string, position = 0): Card {
  return {
    id,
    boardId: 'board-1',
    columnId: 'col-1',
    title: `Card ${id}`,
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

const defaultProps = {
  boardId: 'board-1',
  labels: [] as Label[],
  draggedCard: null,
  selectedCardId: null,
}

describe('ColumnLane — WIP limit enforcement', () => {
  let mockBoardStore: any

  beforeEach(() => {
    setActivePinia(createPinia())

    mockBoardStore = {
      createCard: vi.fn().mockResolvedValue({}),
      moveCard: vi.fn().mockResolvedValue({}),
    }
    vi.mocked(useBoardStore).mockReturnValue(mockBoardStore as any)
  })

  describe('Add Card button state', () => {
    it('is enabled when column has no WIP limit', () => {
      const column = makeColumn({ wipLimit: null })
      const wrapper = mount(ColumnLane, {
        props: { ...defaultProps, column, cards: [] },
        global: {
          stubs: { CardItem: true, CardModal: true, ColumnEditModal: true },
        },
      })

      const btn = wrapper.find('[data-action="toggle-add-card"]')
      expect(btn.attributes('disabled')).toBeUndefined()
    })

    it('is enabled when wipLimit is 0 (defensive: backend prevents this but guard against stale data)', () => {
      // The backend domain layer throws if wipLimit <= 0, so this should never arrive
      // from a healthy API. This test guards against stale/corrupt data reaching the
      // frontend: wipLimit=0 must NOT block card creation (treated as no limit).
      const column = makeColumn({ wipLimit: 0 as unknown as null })
      const wrapper = mount(ColumnLane, {
        props: { ...defaultProps, column, cards: [] },
        global: {
          stubs: { CardItem: true, CardModal: true, ColumnEditModal: true },
        },
      })

      const btn = wrapper.find('[data-action="toggle-add-card"]')
      expect(btn.attributes('disabled')).toBeUndefined()
    })

    it('is enabled when column is under WIP limit', () => {
      const column = makeColumn({ wipLimit: 3 })
      const cards = [makeCard('c1'), makeCard('c2')]
      const wrapper = mount(ColumnLane, {
        props: { ...defaultProps, column, cards },
        global: {
          stubs: { CardItem: true, CardModal: true, ColumnEditModal: true },
        },
      })

      const btn = wrapper.find('[data-action="toggle-add-card"]')
      expect(btn.attributes('disabled')).toBeUndefined()
    })

    it('is disabled when column is exactly at WIP limit', () => {
      const column = makeColumn({ wipLimit: 2 })
      const cards = [makeCard('c1'), makeCard('c2')]
      const wrapper = mount(ColumnLane, {
        props: { ...defaultProps, column, cards },
        global: {
          stubs: { CardItem: true, CardModal: true, ColumnEditModal: true },
        },
      })

      const btn = wrapper.find('[data-action="toggle-add-card"]')
      expect(btn.attributes('disabled')).toBeDefined()
    })

    it('is disabled when column is over WIP limit', () => {
      const column = makeColumn({ wipLimit: 1 })
      const cards = [makeCard('c1'), makeCard('c2')]
      const wrapper = mount(ColumnLane, {
        props: { ...defaultProps, column, cards },
        global: {
          stubs: { CardItem: true, CardModal: true, ColumnEditModal: true },
        },
      })

      const btn = wrapper.find('[data-action="toggle-add-card"]')
      expect(btn.attributes('disabled')).toBeDefined()
    })

    it('is disabled for WIP limit of 1 with one card', () => {
      const column = makeColumn({ wipLimit: 1 })
      const cards = [makeCard('c1')]
      const wrapper = mount(ColumnLane, {
        props: { ...defaultProps, column, cards },
        global: {
          stubs: { CardItem: true, CardModal: true, ColumnEditModal: true },
        },
      })

      const btn = wrapper.find('[data-action="toggle-add-card"]')
      expect(btn.attributes('disabled')).toBeDefined()
    })
  })

  describe('Add Card button click when WIP limit reached', () => {
    it('shows a warning toast instead of opening the form', async () => {
      const column = makeColumn({ wipLimit: 2 })
      const cards = [makeCard('c1'), makeCard('c2')]
      const wrapper = mount(ColumnLane, {
        props: { ...defaultProps, column, cards },
        global: {
          stubs: { CardItem: true, CardModal: true, ColumnEditModal: true },
        },
      })

      // Even though the button is disabled, guard the openCardForm path directly
      // by calling it via the component's exposed behaviour when limit is at capacity
      const toastStore = useToastStore()
      const warningSpy = vi.spyOn(toastStore, 'warning')

      // Manually invoke openCardForm to test the guard (simulates any path reaching it)
      ;(wrapper.vm as any).openCardForm()
      await wrapper.vm.$nextTick()

      expect(warningSpy).toHaveBeenCalledWith(
        expect.stringContaining('WIP limit')
      )
      // Form must NOT open
      expect(wrapper.find('[data-action="add-card-form"]').exists()).toBe(false)
    })

    it('does not call boardStore.createCard when form is blocked', async () => {
      const column = makeColumn({ wipLimit: 2 })
      const cards = [makeCard('c1'), makeCard('c2')]
      const wrapper = mount(ColumnLane, {
        props: { ...defaultProps, column, cards },
        global: {
          stubs: { CardItem: true, CardModal: true, ColumnEditModal: true },
        },
      })

      ;(wrapper.vm as any).openCardForm()
      await wrapper.vm.$nextTick()

      expect(mockBoardStore.createCard).not.toHaveBeenCalled()
    })
  })

  describe('WIP warning banner', () => {
    it('is not shown when column has no WIP limit', () => {
      const wrapper = mount(ColumnLane, {
        props: { ...defaultProps, column: makeColumn({ wipLimit: null }), cards: [] },
        global: { stubs: { CardItem: true, CardModal: true, ColumnEditModal: true } },
      })
      expect(wrapper.find('.td-column-lane__wip-warning').exists()).toBe(false)
    })

    it('is not shown when column is under the WIP limit', () => {
      const wrapper = mount(ColumnLane, {
        props: { ...defaultProps, column: makeColumn({ wipLimit: 3 }), cards: [makeCard('c1')] },
        global: { stubs: { CardItem: true, CardModal: true, ColumnEditModal: true } },
      })
      expect(wrapper.find('.td-column-lane__wip-warning').exists()).toBe(false)
    })

    it('is not shown when column is exactly at WIP limit (exceeded = strictly over)', () => {
      const wrapper = mount(ColumnLane, {
        props: { ...defaultProps, column: makeColumn({ wipLimit: 2 }), cards: [makeCard('c1'), makeCard('c2')] },
        global: { stubs: { CardItem: true, CardModal: true, ColumnEditModal: true } },
      })
      // isWipLimitExceeded uses >, so at-limit does not show the banner
      expect(wrapper.find('.td-column-lane__wip-warning').exists()).toBe(false)
    })

    it('is shown when column is strictly over the WIP limit', () => {
      const wrapper = mount(ColumnLane, {
        props: {
          ...defaultProps,
          column: makeColumn({ wipLimit: 1 }),
          cards: [makeCard('c1'), makeCard('c2')],
        },
        global: { stubs: { CardItem: true, CardModal: true, ColumnEditModal: true } },
      })
      expect(wrapper.find('.td-column-lane__wip-warning').exists()).toBe(true)
    })
  })

  describe('createCard error handling', () => {
    it('shows an error toast when the API returns WipLimitExceeded', async () => {
      const column = makeColumn({ wipLimit: 3 })
      const cards = [makeCard('c1'), makeCard('c2')]

      const wipError = {
        response: {
          data: {
            errorCode: 'WipLimitExceeded',
            message: "Cannot add card, column 'In Progress' has reached its WIP limit of 3",
          },
        },
      }
      mockBoardStore.createCard.mockRejectedValue(wipError)

      const wrapper = mount(ColumnLane, {
        props: { ...defaultProps, column, cards },
        global: { stubs: { CardItem: true, CardModal: true, ColumnEditModal: true } },
      })

      const toastStore = useToastStore()
      const errorSpy = vi.spyOn(toastStore, 'error')

      // Force open the form (column is under limit at 2/3)
      ;(wrapper.vm as any).showCardForm = true
      ;(wrapper.vm as any).newCardTitle = 'New Card'
      await wrapper.vm.$nextTick()

      await (wrapper.vm as any).createCard()

      expect(errorSpy).toHaveBeenCalledWith(
        expect.stringContaining('WIP limit')
      )
    })

    it('shows a generic error toast on unexpected API errors', async () => {
      const column = makeColumn({ wipLimit: null })
      const cards: Card[] = []

      mockBoardStore.createCard.mockRejectedValue(new Error('Network error'))

      const wrapper = mount(ColumnLane, {
        props: { ...defaultProps, column, cards },
        global: { stubs: { CardItem: true, CardModal: true, ColumnEditModal: true } },
      })

      const toastStore = useToastStore()
      const errorSpy = vi.spyOn(toastStore, 'error')

      ;(wrapper.vm as any).showCardForm = true
      ;(wrapper.vm as any).newCardTitle = 'New Card'
      await wrapper.vm.$nextTick()

      await (wrapper.vm as any).createCard()

      expect(errorSpy).toHaveBeenCalled()
    })
  })
})

describe('ColumnLane — card modal', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.mocked(useBoardStore).mockReturnValue({
      createCard: vi.fn().mockResolvedValue({}),
      moveCard: vi.fn().mockResolvedValue({}),
    } as any)
  })

  it('opens the card modal when handleCardClick is called', async () => {
    const column = makeColumn()
    const card = makeCard('c1')
    const wrapper = mount(ColumnLane, {
      props: { ...defaultProps, column, cards: [card] },
      global: { stubs: { CardItem: true, CardModal: true, ColumnEditModal: true } },
    })

    await (wrapper.vm as any).handleCardClick(card)
    await wrapper.vm.$nextTick()

    expect((wrapper.vm as any).selectedCard).toEqual(card)
    expect((wrapper.vm as any).showCardModal).toBe(true)
  })

  it('closes the card modal and clears selectedCard when handleModalClose is called', async () => {
    const column = makeColumn()
    const card = makeCard('c1')
    const wrapper = mount(ColumnLane, {
      props: { ...defaultProps, column, cards: [card] },
      global: { stubs: { CardItem: true, CardModal: true, ColumnEditModal: true } },
    })

    await (wrapper.vm as any).handleCardClick(card)
    await (wrapper.vm as any).handleModalClose()
    await wrapper.vm.$nextTick()

    expect((wrapper.vm as any).selectedCard).toBeNull()
    expect((wrapper.vm as any).showCardModal).toBe(false)
  })
})
