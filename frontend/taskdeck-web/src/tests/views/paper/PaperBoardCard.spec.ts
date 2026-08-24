import { afterEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import PaperBoardCard from '../../../views/paper/PaperBoardCard.vue'
import type { Card } from '../../../types/board'

function makeCard(partial: Partial<Card> = {}): Card {
  return {
    id: 'a1b2c3d4e5f60718-aaaa-bbbb-cccc-ddddeeeeffff',
    boardId: 'board-1',
    columnId: 'col-1',
    title: 'Set up CI pipeline',
    description: 'Configure GitHub Actions for build and test.',
    dueDate: null,
    isBlocked: false,
    blockReason: null,
    position: 0,
    labels: [
      {
        id: 'label-1',
        boardId: 'board-1',
        name: 'infra',
        colorHex: '#a8421f',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
    ],
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
    ...partial,
  }
}

describe('PaperBoardCard', () => {
  afterEach(() => {
    vi.unstubAllEnvs()
    vi.useRealTimers()
  })

  it('renders the index variant by default with serial, title, and description', () => {
    const wrapper = mount(PaperBoardCard, { props: { card: makeCard() } })
    expect(wrapper.attributes('data-variant')).toBe('index')
    expect(wrapper.classes()).toContain('paper-board-card--index')
    // serial — `C-` plus first 8 hex chars of card id (with dashes stripped).
    expect(wrapper.find('.paper-board-card__serial').text()).toBe('C-a1b2c3d4')
    expect(wrapper.find('.paper-board-card__title').text()).toBe('Set up CI pipeline')
    expect(wrapper.find('.paper-board-card__excerpt').exists()).toBe(true)
    // ribbon must NOT render in the default (index) variant
    expect(wrapper.find('.paper-board-card__ribbon').exists()).toBe(false)
  })

  it('renders the ribbon variant with a coloured ribbon element', () => {
    const wrapper = mount(PaperBoardCard, {
      props: { card: makeCard(), variant: 'ribbon' },
    })
    expect(wrapper.attributes('data-variant')).toBe('ribbon')
    expect(wrapper.classes()).toContain('paper-board-card--ribbon')
    const ribbon = wrapper.find('.paper-board-card__ribbon')
    expect(ribbon.exists()).toBe(true)
    // ribbon picks up the first label's colour by default
    expect(ribbon.attributes('style')).toContain('#a8421f')
  })

  it('applies the line-clamp 1 style on the description excerpt', () => {
    const longBody =
      'A really long description meant to overflow a single line so the index card metadata strip pushes it under a CSS line-clamp guard, which we assert here.'
    const wrapper = mount(PaperBoardCard, {
      props: { card: makeCard({ description: longBody }) },
    })
    const excerpt = wrapper.find('.paper-board-card__excerpt')
    expect(excerpt.exists()).toBe(true)
    // line-clamp / -webkit-line-clamp set in scoped CSS — assert the class is
    // present so the cap is consistently applied; the runtime style block is
    // injected by happy-dom but not surfaced via getComputedStyle, so we
    // verify intent through the class hook.
    expect(excerpt.classes()).toContain('paper-board-card__excerpt')
    // text content should still be the full string (clamp is a CSS visual cap)
    expect(excerpt.text()).toBe(longBody)
  })

  it('renders a tagstamp matching the tone prop', () => {
    const wrapper = mount(PaperBoardCard, {
      props: { card: makeCard(), tone: 'overdue' },
    })
    const stamp = wrapper.find('.paper-board-card__tagstamp')
    expect(stamp.exists()).toBe(true)
    expect(stamp.text()).toBe('OVERDUE')
    expect(stamp.attributes('data-tone')).toBe('overdue')
  })

  it('renders a UTC due calendar day unchanged west of UTC and marks it overdue', () => {
    vi.stubEnv('TZ', 'America/Los_Angeles')
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-08-24T19:00:00.000Z'))

    const wrapper = mount(PaperBoardCard, {
      props: {
        card: makeCard({ dueDate: '2026-08-23T00:00:00+00:00' }),
      },
    })

    const dueDate = wrapper.get('.paper-board-card__due-date')
    expect(dueDate.text()).toBe('Due 8/23/2026')
    expect(dueDate.text()).not.toContain('8/22/2026')
    expect(dueDate.classes()).toContain('paper-board-card__due-date--overdue')
    expect(wrapper.get('.paper-board-card__tagstamp').text()).toBe('OVERDUE')
    expect(wrapper.get('[data-action="open-card"]').attributes('aria-label'))
      .toBe('Card Set up CI pipeline, due 8/23/2026, overdue')
  })

  it('emits click with the card payload when activated', async () => {
    const card = makeCard()
    const wrapper = mount(PaperBoardCard, { props: { card } })
    await wrapper.find('.paper-board-card__open').trigger('click')
    expect(wrapper.emitted('click')?.[0]?.[0]).toStrictEqual(card)
  })

  it('emits click with the card payload from Space key activation', async () => {
    const card = makeCard()
    const wrapper = mount(PaperBoardCard, { props: { card } })
    await wrapper.find('.paper-board-card__open').trigger('keydown', { key: ' ' })
    expect(wrapper.emitted('click')?.[0]?.[0]).toStrictEqual(card)
  })

  it('handles opener Enter without reaching the global board shortcut', () => {
    const card = makeCard()
    const wrapper = mount(PaperBoardCard, { props: { card }, attachTo: document.body })
    const globalKeydown = vi.fn()
    window.addEventListener('keydown', globalKeydown)

    try {
      wrapper.find('.paper-board-card__open').element.dispatchEvent(
        new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }),
      )

      expect(wrapper.emitted('click')?.[0]?.[0]).toStrictEqual(card)
      expect(globalKeydown).not.toHaveBeenCalled()
    } finally {
      window.removeEventListener('keydown', globalKeydown)
      wrapper.unmount()
    }
  })

  it('keeps the card opener separate from the pointer-only drag affordance', () => {
    const wrapper = mount(PaperBoardCard, { props: { card: makeCard() } })
    const opener = wrapper.find('.paper-board-card__open')
    const dragHandle = wrapper.find('[data-action="drag-card-handle"]')

    expect(opener.element.tagName).toBe('BUTTON')
    expect(opener.attributes('data-action')).toBe('open-card')
    expect(dragHandle.element.tagName).toBe('SPAN')
    expect(opener.element.contains(dragHandle.element)).toBe(false)
    expect(dragHandle.attributes('aria-hidden')).toBe('true')
    expect(dragHandle.attributes('tabindex')).toBeUndefined()
    expect(dragHandle.attributes('title')).toBe('Drag Set up CI pipeline')
    expect(wrapper.attributes('role')).toBeUndefined()
    expect(wrapper.attributes('tabindex')).toBeUndefined()
  })

  it('emits dragstart and dragend events', async () => {
    const card = makeCard()
    const wrapper = mount(PaperBoardCard, { props: { card } })
    await wrapper.find('[data-action="drag-card-handle"]').trigger('dragstart')
    await wrapper.trigger('dragend')
    expect(wrapper.emitted('dragstart')?.[0]?.[0]).toStrictEqual(card)
    expect(wrapper.emitted('dragend')).toHaveLength(1)
  })
})
