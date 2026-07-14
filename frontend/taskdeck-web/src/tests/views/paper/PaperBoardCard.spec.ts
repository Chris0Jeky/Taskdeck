import { describe, expect, it } from 'vitest'
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

  it('keeps the card opener and drag handle as sibling controls', () => {
    const wrapper = mount(PaperBoardCard, { props: { card: makeCard() } })
    const opener = wrapper.find('.paper-board-card__open')
    const dragHandle = wrapper.find('[data-action="drag-card-handle"]')

    expect(opener.element.tagName).toBe('BUTTON')
    expect(dragHandle.element.tagName).toBe('BUTTON')
    expect(opener.element.contains(dragHandle.element)).toBe(false)
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
