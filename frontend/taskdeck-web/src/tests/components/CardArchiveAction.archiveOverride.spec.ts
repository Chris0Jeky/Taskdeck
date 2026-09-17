import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import CardArchiveAction from '../../components/board/CardArchiveAction.vue'
import type { Card } from '../../types/board'

const mocks = vi.hoisted(() => ({
  setCardArchived: vi.fn(),
  previewDetach: vi.fn(),
}))

vi.mock('../../api/cardsApi', () => ({
  cardsApi: { previewDetach: mocks.previewDetach },
}))
vi.mock('../../store/toastStore', () => ({
  useToastStore: () => ({ error: vi.fn() }),
}))
vi.mock('../../store/boardStore', () => ({
  useBoardStore: () => ({
    currentBoard: { id: 'board-1', canWrite: true, isArchived: false },
    setCardArchived: mocks.setCardArchived,
  }),
}))

const staleCard = {
  id: 'card-1',
  boardId: 'board-1',
  columnId: 'column-1',
  title: 'Archive recovery',
  description: '',
  labels: [],
  isArchived: false,
  updatedAt: 'v1',
} as unknown as Card

describe('CardArchiveAction archive-state override recovery (GH-3023)', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    mocks.setCardArchived.mockResolvedValue({
      ...staleCard,
      isArchived: false,
      updatedAt: 'v3',
    })
  })

  it('explains the stale lifecycle snapshot until an authoritative reopen supplies the card version', async () => {
    const view = mount(CardArchiveAction, {
      props: {
        card: staleCard,
        archived: true,
        disabled: true,
        canWrite: true,
      },
    })

    const frozenRestore = view.get('button')
    expect(frozenRestore.text()).toBe('Restore card')
    expect(frozenRestore.attributes('disabled')).toBeDefined()
    expect(view.get('[data-testid="card-archive-reopen-required"]').text()).toContain('Close and reopen')
    expect(view.text()).not.toContain('Save or discard your changes')

    const authoritativeCard = {
      ...staleCard,
      isArchived: true,
      updatedAt: 'v2',
    }
    await view.setProps({
      card: authoritativeCard,
      archived: true,
      disabled: false,
    })

    expect(view.find('[data-testid="card-archive-reopen-required"]').exists()).toBe(false)
    const restoredControl = view.get('button')
    expect(restoredControl.text()).toBe('Restore card')
    expect(restoredControl.attributes('disabled')).toBeUndefined()

    await restoredControl.trigger('click')
    await flushPromises()

    expect(mocks.setCardArchived).toHaveBeenCalledExactlyOnceWith(
      'board-1',
      'card-1',
      false,
      'v2',
      undefined,
    )
    expect(view.emitted('changed')).toHaveLength(1)
  })

  it('keeps ordinary dirty-state guidance when the card snapshot is authoritative', () => {
    const view = mount(CardArchiveAction, {
      props: {
        card: { ...staleCard, isArchived: true, updatedAt: 'v2' },
        archived: true,
        disabled: true,
        canWrite: true,
      },
    })

    expect(view.find('[data-testid="card-archive-reopen-required"]').exists()).toBe(false)
    expect(view.text()).toContain('Save or discard your changes before archiving')
  })
})
