import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import BoardToolbar from '../../components/board/BoardToolbar.vue'

function mountToolbar(propsOverrides = {}) {
  return mount(BoardToolbar, {
    props: {
      boardName: 'Test Board',
      boardDescription: 'A board for testing',
      isDemoBoard: false,
      presenceMembers: [],
      showFilterPanel: false,
      filteredCardCount: 5,
      totalCardCount: 5,
      ...propsOverrides,
    },
  })
}

describe('BoardToolbar', () => {
  it('renders board name and description', () => {
    const wrapper = mountToolbar()
    expect(wrapper.text()).toContain('Test Board')
    expect(wrapper.text()).toContain('A board for testing')
  })

  it('shows demo badge when isDemoBoard is true', () => {
    const wrapper = mountToolbar({ isDemoBoard: true })
    expect(wrapper.text()).toContain('Demo board')
  })

  it('does not show demo badge when isDemoBoard is false', () => {
    const wrapper = mountToolbar({ isDemoBoard: false })
    expect(wrapper.text()).not.toContain('Demo board')
  })

  it('shows "No active collaborators" when presence is empty', () => {
    const wrapper = mountToolbar({ presenceMembers: [] })
    expect(wrapper.text()).toContain('No active collaborators')
  })

  it('shows presence members', () => {
    const wrapper = mountToolbar({
      presenceMembers: [
        { userId: 'user-abc-123', displayName: 'Alice', editingCardId: null },
      ],
    })
    expect(wrapper.text()).toContain('Alice')
    expect(wrapper.text()).not.toContain('No active collaborators')
  })

  it('shows (editing) indicator for members editing a card', () => {
    const wrapper = mountToolbar({
      presenceMembers: [
        { userId: 'user-1', displayName: 'Bob', editingCardId: 'card-1' },
      ],
    })
    expect(wrapper.text()).toContain('(editing)')
  })

  it('emits back when back button is clicked', async () => {
    const wrapper = mountToolbar()
    await wrapper.find('button').trigger('click')
    expect(wrapper.emitted('back')).toHaveLength(1)
  })

  it('emits toggleColumnForm when Add Column is clicked', async () => {
    const wrapper = mountToolbar()
    const btn = wrapper.findAll('button').find(b => b.text().includes('Add Column'))
    await btn?.trigger('click')
    expect(wrapper.emitted('toggleColumnForm')).toHaveLength(1)
  })

  it('emits showLabelManager when Labels button is clicked', async () => {
    const wrapper = mountToolbar()
    const btn = wrapper.findAll('button').find(b => b.text().includes('Labels'))
    await btn?.trigger('click')
    expect(wrapper.emitted('showLabelManager')).toHaveLength(1)
  })

  it('emits showStarterPackCatalog when Starter Packs button is clicked', async () => {
    const wrapper = mountToolbar()
    const btn = wrapper.findAll('button').find(b => b.text().includes('Starter Packs'))
    await btn?.trigger('click')
    expect(wrapper.emitted('showStarterPackCatalog')).toHaveLength(1)
  })
})
