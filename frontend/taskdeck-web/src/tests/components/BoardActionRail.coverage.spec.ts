import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import BoardActionRail from '../../components/board/BoardActionRail.vue'

describe('BoardActionRail — button emissions', () => {
  it('renders all action buttons', () => {
    const wrapper = mount(BoardActionRail)

    expect(wrapper.text()).toContain('Board Actions')
    expect(wrapper.text()).toContain('Capture here')
    expect(wrapper.text()).toContain('Ask assistant')
    expect(wrapper.text()).toContain('Review proposals')
    expect(wrapper.text()).toContain('Open Inbox')
    expect(wrapper.text()).toContain('Add card')
  })

  it('emits capture when Capture here button is clicked', async () => {
    const wrapper = mount(BoardActionRail)

    const btn = wrapper.findAll('button').find((b) => b.text().trim() === 'Capture here')
    await btn!.trigger('click')

    expect(wrapper.emitted('capture')).toHaveLength(1)
  })

  it('emits chat when Ask assistant button is clicked', async () => {
    const wrapper = mount(BoardActionRail)

    const btn = wrapper.findAll('button').find((b) => b.text().trim() === 'Ask assistant')
    await btn!.trigger('click')

    expect(wrapper.emitted('chat')).toHaveLength(1)
  })

  it('emits review when Review proposals button is clicked', async () => {
    const wrapper = mount(BoardActionRail)

    const btn = wrapper.findAll('button').find((b) => b.text().trim() === 'Review proposals')
    await btn!.trigger('click')

    expect(wrapper.emitted('review')).toHaveLength(1)
  })

  it('emits inbox when Open Inbox button is clicked', async () => {
    const wrapper = mount(BoardActionRail)

    const btn = wrapper.findAll('button').find((b) => b.text().trim() === 'Open Inbox')
    await btn!.trigger('click')

    expect(wrapper.emitted('inbox')).toHaveLength(1)
  })

  it('emits addCard when Add card button is clicked', async () => {
    const wrapper = mount(BoardActionRail)

    const btn = wrapper.findAll('button').find((b) => b.text().trim() === 'Add card')
    await btn!.trigger('click')

    expect(wrapper.emitted('addCard')).toHaveLength(1)
  })

  it('shows the review-first trust hint', () => {
    const wrapper = mount(BoardActionRail)

    expect(wrapper.text()).toContain('Only approved changes land on this board.')
  })

  it('has the data-board-action-rail attribute for test targeting', () => {
    const wrapper = mount(BoardActionRail)

    expect(wrapper.find('[data-board-action-rail]').exists()).toBe(true)
  })

  it('distinguishes Add card button as primary', () => {
    const wrapper = mount(BoardActionRail)

    const addCardBtn = wrapper.findAll('button').find((b) => b.text().trim() === 'Add card')
    expect(addCardBtn!.classes()).toContain('td-action-rail__btn--primary')
  })
})
