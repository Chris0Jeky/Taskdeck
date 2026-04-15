import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import CardItem from '../../components/board/CardItem.vue'
import type { Card, Column } from '../../types/board'

function createCard(overrides: Partial<Card> = {}): Card {
  const now = new Date().toISOString()
  return {
    id: 'card-1',
    boardId: 'board-1',
    columnId: 'column-1',
    title: 'Test Card',
    description: 'A description',
    dueDate: null,
    isBlocked: false,
    blockReason: null,
    position: 0,
    labels: [],
    createdAt: now,
    updatedAt: now,
    ...overrides,
  }
}

function createColumns(): Column[] {
  const now = new Date().toISOString()
  return [
    { id: 'column-1', boardId: 'board-1', name: 'Todo', position: 0, wipLimit: null, createdAt: now, updatedAt: now },
    { id: 'column-2', boardId: 'board-1', name: 'In Progress', position: 1, wipLimit: null, createdAt: now, updatedAt: now },
    { id: 'column-3', boardId: 'board-1', name: 'Done', position: 2, wipLimit: null, createdAt: now, updatedAt: now },
  ]
}

describe('CardItem — context move menu', () => {
  it('shows move-to menu button when multiple columns exist', () => {
    const wrapper = mount(CardItem, {
      props: { card: createCard(), columns: createColumns() },
    })

    const moveBtn = wrapper.find('[data-action="card-move-menu-trigger"]')
    expect(moveBtn.exists()).toBe(true)
    expect(moveBtn.attributes('aria-label')).toBe('Move to column')
    expect(moveBtn.attributes('aria-haspopup')).toBe('true')
    expect(moveBtn.attributes('aria-expanded')).toBe('false')
  })

  it('hides move-to menu button when only one column exists', () => {
    const columns = [createColumns()[0]]
    const wrapper = mount(CardItem, {
      props: { card: createCard(), columns },
    })

    expect(wrapper.find('[data-action="card-move-menu-trigger"]').exists()).toBe(false)
  })

  it('hides move-to menu button when columns prop is not provided', () => {
    const wrapper = mount(CardItem, {
      props: { card: createCard() },
    })

    expect(wrapper.find('[data-action="card-move-menu-trigger"]').exists()).toBe(false)
  })

  it('toggles the move menu open/closed on click', async () => {
    const wrapper = mount(CardItem, {
      props: { card: createCard(), columns: createColumns() },
    })

    const moveBtn = wrapper.find('[data-action="card-move-menu-trigger"]')

    await moveBtn.trigger('click')
    expect(wrapper.find('.td-card-move-menu').exists()).toBe(true)
    expect(moveBtn.attributes('aria-expanded')).toBe('true')

    await moveBtn.trigger('click')
    expect(wrapper.find('.td-card-move-menu').exists()).toBe(false)
    expect(moveBtn.attributes('aria-expanded')).toBe('false')
  })

  it('shows all columns in the move menu with current column marked', async () => {
    const wrapper = mount(CardItem, {
      props: { card: createCard({ columnId: 'column-1' }), columns: createColumns() },
    })

    await wrapper.find('[data-action="card-move-menu-trigger"]').trigger('click')

    const menu = wrapper.find('.td-card-move-menu')
    expect(menu.exists()).toBe(true)
    expect(menu.text()).toContain('Move to...')
    expect(menu.text()).toContain('Todo')
    expect(menu.text()).toContain('In Progress')
    expect(menu.text()).toContain('Done')
    expect(menu.text()).toContain('(current)')

    // Current column item should be disabled
    const currentItem = menu.find('.td-card-move-menu__item--current')
    expect(currentItem.exists()).toBe(true)
    expect(currentItem.attributes('disabled')).toBeDefined()
  })

  it('emits move-to event with target column when a column is selected', async () => {
    const card = createCard({ columnId: 'column-1' })
    const wrapper = mount(CardItem, {
      props: { card, columns: createColumns() },
    })

    await wrapper.find('[data-action="card-move-menu-trigger"]').trigger('click')

    const menuItems = wrapper.findAll('.td-card-move-menu__item')
    // Click "In Progress" (second item, not disabled)
    const inProgressItem = menuItems.find((item) => item.text().includes('In Progress'))
    expect(inProgressItem).toBeDefined()
    await inProgressItem!.trigger('click')

    expect(wrapper.emitted('move-to')).toBeTruthy()
    expect(wrapper.emitted('move-to')![0]).toEqual([card, 'column-2'])
  })

  it('closes move menu when Escape is pressed', async () => {
    const wrapper = mount(CardItem, {
      props: { card: createCard(), columns: createColumns() },
    })

    await wrapper.find('[data-action="card-move-menu-trigger"]').trigger('click')
    expect(wrapper.find('.td-card-move-menu').exists()).toBe(true)

    await wrapper.find('.td-card-move-menu').trigger('keydown', { key: 'Escape' })
    expect(wrapper.find('.td-card-move-menu').exists()).toBe(false)
  })

  it('closes move menu after selecting a column', async () => {
    const wrapper = mount(CardItem, {
      props: { card: createCard(), columns: createColumns() },
    })

    await wrapper.find('[data-action="card-move-menu-trigger"]').trigger('click')
    expect(wrapper.find('.td-card-move-menu').exists()).toBe(true)

    const menuItems = wrapper.findAll('.td-card-move-menu__item')
    const doneItem = menuItems.find((item) => item.text().includes('Done'))
    await doneItem!.trigger('click')

    expect(wrapper.find('.td-card-move-menu').exists()).toBe(false)
  })
})

describe('CardItem — blocked state', () => {
  it('shows blocked badge when card is blocked', () => {
    const wrapper = mount(CardItem, {
      props: { card: createCard({ isBlocked: true, blockReason: 'Waiting for deps' }) },
    })

    expect(wrapper.find('.td-board-card__badge--blocked').exists()).toBe(true)
    expect(wrapper.text()).toContain('Blocked')
  })

  it('does not show blocked badge when card is not blocked', () => {
    const wrapper = mount(CardItem, {
      props: { card: createCard({ isBlocked: false }) },
    })

    expect(wrapper.find('.td-board-card__badge--blocked').exists()).toBe(false)
  })
})

describe('CardItem — labels', () => {
  it('renders card labels with correct color', () => {
    const card = createCard({
      labels: [
        { id: 'label-1', name: 'Urgent', colorHex: '#ff0000', boardId: 'board-1', createdAt: new Date().toISOString(), updatedAt: new Date().toISOString() },
        { id: 'label-2', name: 'Feature', colorHex: '#00ff00', boardId: 'board-1', createdAt: new Date().toISOString(), updatedAt: new Date().toISOString() },
      ],
    })
    const wrapper = mount(CardItem, { props: { card } })

    const labels = wrapper.findAll('.td-board-card__label')
    expect(labels).toHaveLength(2)
    expect(labels[0].text()).toBe('Urgent')
    expect(labels[0].attributes('style')).toContain('background-color: #ff0000')
    expect(labels[1].text()).toBe('Feature')
  })

  it('does not render labels section when card has no labels', () => {
    const wrapper = mount(CardItem, {
      props: { card: createCard({ labels: [] }) },
    })

    expect(wrapper.find('.td-board-card__labels').exists()).toBe(false)
  })
})

describe('CardItem — selection and click', () => {
  it('applies selected class and aria-selected when isSelected is true', () => {
    const wrapper = mount(CardItem, {
      props: { card: createCard(), isSelected: true },
    })

    const cardEl = wrapper.find('.td-board-card')
    expect(cardEl.classes()).toContain('td-board-card--selected')
    expect(cardEl.attributes('aria-selected')).toBe('true')
  })

  it('emits click event when card is clicked', async () => {
    const card = createCard()
    const wrapper = mount(CardItem, { props: { card } })

    await wrapper.find('.td-board-card').trigger('click')

    expect(wrapper.emitted('click')).toBeTruthy()
    expect(wrapper.emitted('click')![0]).toEqual([card])
  })

  it('emits click event on Enter key press', async () => {
    const card = createCard()
    const wrapper = mount(CardItem, { props: { card } })

    await wrapper.find('.td-board-card').trigger('keydown.enter')

    expect(wrapper.emitted('click')).toBeTruthy()
    expect(wrapper.emitted('click')![0]).toEqual([card])
  })

  it('emits click event on Space key press', async () => {
    const card = createCard()
    const wrapper = mount(CardItem, { props: { card } })

    await wrapper.find('.td-board-card').trigger('keydown.space')

    expect(wrapper.emitted('click')).toBeTruthy()
  })

  it('has tabindex for keyboard navigation', () => {
    const wrapper = mount(CardItem, { props: { card: createCard() } })

    expect(wrapper.find('.td-board-card').attributes('tabindex')).toBe('0')
  })

  it('has role="option" for accessibility', () => {
    const wrapper = mount(CardItem, { props: { card: createCard() } })

    expect(wrapper.find('.td-board-card').attributes('role')).toBe('option')
  })
})

describe('CardItem — description display', () => {
  it('renders description when present', () => {
    const wrapper = mount(CardItem, {
      props: { card: createCard({ description: 'Important task details' }) },
    })

    expect(wrapper.find('.td-board-card__description').exists()).toBe(true)
    expect(wrapper.text()).toContain('Important task details')
  })

  it('does not render description when empty', () => {
    const wrapper = mount(CardItem, {
      props: { card: createCard({ description: '' }) },
    })

    expect(wrapper.find('.td-board-card__description').exists()).toBe(false)
  })

  it('does not render description when null', () => {
    const wrapper = mount(CardItem, {
      props: { card: createCard({ description: null }) },
    })

    expect(wrapper.find('.td-board-card__description').exists()).toBe(false)
  })
})
