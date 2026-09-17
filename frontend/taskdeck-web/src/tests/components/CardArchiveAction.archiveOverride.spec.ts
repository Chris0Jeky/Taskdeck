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

  it('directs an archived card through Archived cards before an authoritative reopen', async () => {
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
    const guidance = view.get('[data-testid="card-archive-reopen-required"]').text()
    expect(guidance).toContain('restore it from Archived cards')
    expect(guidance).toContain('then reopen it')
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

  it('uses direct close-and-reopen guidance after a restore commits over a stale archived snapshot', () => {
    const view = mount(CardArchiveAction, {
      props: {
        card: { ...staleCard, isArchived: true, updatedAt: 'v2' },
        archived: false,
        disabled: true,
        canWrite: true,
      },
    })

    expect(view.get('button').text()).toBe('Archive card')
    const guidance = view.get('[data-testid="card-archive-reopen-required"]').text()
    expect(guidance).toContain('Close and reopen the editor')
    expect(guidance).not.toContain('Archived cards')
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
