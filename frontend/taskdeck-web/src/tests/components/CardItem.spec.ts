import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
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
  it('exposes an explicit enlarged drag handle control', () => {
    const wrapper = mount(CardItem, {
      props: {
        card: createCard(),
      },
    })

    const handle = wrapper.get('.td-card-drag-handle')
    expect(handle.attributes('data-action')).toBe('drag-card-handle')
    expect(handle.attributes('draggable')).toBe('true')
    expect(handle.classes()).toContain('w-[calc(100%+1rem)]')
    expect(handle.classes()).toContain('min-h-10')
    expect(handle.classes()).toContain('px-3')
    expect(handle.classes()).toContain('py-2')
    expect(handle.text()).toContain('Drag card')
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

  it('allows dragstart from the dedicated drag handle', async () => {
    const card = createCard()
    const wrapper = mount(CardItem, {
      props: {
        card,
      },
    })

    const setData = vi.fn()
    await wrapper.get('[data-action="drag-card-handle"]').trigger('dragstart', {
      dataTransfer: { effectAllowed: 'move', setData },
    })

    expect(setData).toHaveBeenCalledWith('text/plain', card.id)
    expect(wrapper.emitted('dragstart')).toEqual([[card]])
  })
})

describe('CardItem drag handle — text selection clearing', () => {
  let removeAllRanges: ReturnType<typeof vi.fn>

  beforeEach(() => {
    removeAllRanges = vi.fn()
    vi.stubGlobal('getSelection', () => ({ removeAllRanges }))
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('calls removeAllRanges on drag handle mousedown to clear prior text selection', async () => {
    const wrapper = mount(CardItem, {
      props: { card: createCard() },
    })

    await wrapper.get('[data-action="drag-card-handle"]').trigger('mousedown')

    expect(removeAllRanges).toHaveBeenCalledOnce()
  })

  it('does not throw when getSelection returns null', async () => {
    vi.stubGlobal('getSelection', () => null)

    const wrapper = mount(CardItem, {
      props: { card: createCard() },
    })

    await expect(
      wrapper.get('[data-action="drag-card-handle"]').trigger('mousedown'),
    ).resolves.not.toThrow()
  })
})
