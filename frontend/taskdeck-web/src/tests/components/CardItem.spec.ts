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
  it('clears an active text selection when the drag handle is mousedown-ed', async () => {
    const card = createCard()
    const wrapper = mount(CardItem, { props: { card } })

    // Build a real DOM range over the card title text and add it to the selection
    const selection = window.getSelection()
    if (!selection) return // JSDOM always provides getSelection; guard for type safety

    const titleEl = wrapper.get('.td-board-card__title').element
    const range = document.createRange()
    range.selectNodeContents(titleEl)
    selection.removeAllRanges()
    selection.addRange(range)
    expect(selection.toString()).toBe(card.title)

    // Trigger mousedown on the drag handle
    await wrapper.get('[data-action="drag-card-handle"]').trigger('mousedown')

    // Selection must be cleared
    expect(selection.toString()).toBe('')
  })

  it('does not throw when getSelection returns null', async () => {
    vi.stubGlobal('getSelection', () => null)

    const wrapper = mount(CardItem, {
      props: { card: createCard() },
    })

    await expect(
      wrapper.get('[data-action="drag-card-handle"]').trigger('mousedown'),
    ).resolves.not.toThrow()

    vi.unstubAllGlobals()
  })
})
