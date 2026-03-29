import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import BoardActionRail from '../../components/board/BoardActionRail.vue'

describe('BoardActionRail', () => {
  function mountRail() {
    return mount(BoardActionRail)
  }

  it('renders all action buttons', () => {
    const wrapper = mountRail()
    const text = wrapper.text()
    expect(text).toContain('Capture here')
    expect(text).toContain('Ask assistant')
    expect(text).toContain('Review proposals')
    expect(text).toContain('Open Inbox')
    expect(text).toContain('Add card')
  })

  it('renders the review-first guidance message', () => {
    const wrapper = mountRail()
    expect(wrapper.text()).toContain('Only approved changes land on this board.')
  })

  it('emits capture when Capture here is clicked', async () => {
    const wrapper = mountRail()
    const btn = wrapper.findAll('button').find(b => b.text().trim() === 'Capture here')
    await btn?.trigger('click')
    expect(wrapper.emitted('capture')).toHaveLength(1)
  })

  it('emits chat when Ask assistant is clicked', async () => {
    const wrapper = mountRail()
    const btn = wrapper.findAll('button').find(b => b.text().trim() === 'Ask assistant')
    await btn?.trigger('click')
    expect(wrapper.emitted('chat')).toHaveLength(1)
  })

  it('emits review when Review proposals is clicked', async () => {
    const wrapper = mountRail()
    const btn = wrapper.findAll('button').find(b => b.text().trim() === 'Review proposals')
    await btn?.trigger('click')
    expect(wrapper.emitted('review')).toHaveLength(1)
  })

  it('emits inbox when Open Inbox is clicked', async () => {
    const wrapper = mountRail()
    const btn = wrapper.findAll('button').find(b => b.text().trim() === 'Open Inbox')
    await btn?.trigger('click')
    expect(wrapper.emitted('inbox')).toHaveLength(1)
  })

  it('emits addCard when Add card is clicked', async () => {
    const wrapper = mountRail()
    const btn = wrapper.findAll('button').find(b => b.text().trim() === 'Add card')
    await btn?.trigger('click')
    expect(wrapper.emitted('addCard')).toHaveLength(1)
  })

  it('has the data-board-action-rail attribute for test selectors', () => {
    const wrapper = mountRail()
    expect(wrapper.find('[data-board-action-rail]').exists()).toBe(true)
  })
})
