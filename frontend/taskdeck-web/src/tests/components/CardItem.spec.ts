import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import CardItem from '../../components/board/CardItem.vue'
import type { Card } from '../../types/board'

function createCard(): Card {
  const now = new Date().toISOString()
  return {
    id: 'card-1',
    boardId: 'board-1',
    columnId: 'column-1',
    title: 'Card title',
    description: 'Card description',
    dueDate: null,
    isBlocked: false,
    blockReason: null,
    position: 0,
    labels: [],
    createdAt: now,
    updatedAt: now,
  }
}

describe('CardItem drag guardrails', () => {
  it('exposes a broad drag surface handle', () => {
    const wrapper = mount(CardItem, {
      props: {
        card: createCard(),
      },
    })

    const dragSurface = wrapper.get('.td-card-drag-surface')
    expect(dragSurface.attributes('data-action')).toBe('drag-card-handle')
    expect(dragSurface.attributes('draggable')).toBe('true')
  })

  it('blocks dragstart when not initiated from drag handle', async () => {
    const wrapper = mount(CardItem, {
      props: {
        card: createCard(),
      },
    })

    const setData = vi.fn()
    await wrapper.get('[data-card-id]').trigger('dragstart', {
      dataTransfer: { effectAllowed: 'move', setData },
    })

    expect(wrapper.emitted('dragstart')).toBeFalsy()
    expect(setData).not.toHaveBeenCalled()
  })

  it('allows dragstart from card content inside the drag surface', async () => {
    const card = createCard()
    const wrapper = mount(CardItem, {
      props: {
        card,
      },
    })

    const setData = vi.fn()
    await wrapper.get('h4').trigger('dragstart', {
      dataTransfer: { effectAllowed: 'move', setData },
    })

    expect(setData).toHaveBeenCalledWith('text/plain', card.id)
    expect(wrapper.emitted('dragstart')).toEqual([[card]])
  })
})
