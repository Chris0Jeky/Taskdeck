import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import ActivitySelector from '../../components/activity/ActivitySelector.vue'

const mockSessionStore = reactive({
  username: 'alice',
})

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: () => mockSessionStore,
}))

const defaultProps = {
  viewMode: 'board' as const,
  selectedBoardId: '',
  selectedEntityType: '' as const,
  selectedEntityBoardId: '',
  selectedEntityId: '',
  limit: 50,
  loadingEntitySource: false,
  boardOptions: [
    { id: 'b1', label: 'Board One' },
    { id: 'b2', label: 'Board Two' },
  ],
  requiresEntityBoardContext: false,
  entityOptions: [],
  canFetch: true,
  selectedIdForCopy: '',
  selectedIdLabel: '',
}

describe('ActivitySelector', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockSessionStore.username = 'alice'
  })

  it('renders view mode selector with all options', () => {
    const wrapper = mount(ActivitySelector, { props: defaultProps })
    const modeSelect = wrapper.find('#activity-view-mode')
    expect(modeSelect.exists()).toBe(true)
    const options = modeSelect.findAll('option')
    expect(options).toHaveLength(3)
    expect(options[0].text()).toBe('Board History')
    expect(options[1].text()).toBe('Entity History')
    expect(options[2].text()).toBe('User History')
  })

  it('emits update:viewMode when view mode changes', async () => {
    const wrapper = mount(ActivitySelector, { props: defaultProps })
    await wrapper.find('#activity-view-mode').setValue('entity')
    expect(wrapper.emitted('update:viewMode')?.[0]).toEqual(['entity'])
  })

  it('shows board selector when viewMode is "board"', () => {
    const wrapper = mount(ActivitySelector, { props: defaultProps })
    const boardSelect = wrapper.find('#activity-board-select')
    expect(boardSelect.exists()).toBe(true)
    const options = boardSelect.findAll('option')
    // 1 disabled placeholder + 2 board options
    expect(options).toHaveLength(3)
    expect(options[1].text()).toBe('Board One')
    expect(options[2].text()).toBe('Board Two')
  })

  it('emits update:selectedBoardId when board is changed', async () => {
    const wrapper = mount(ActivitySelector, { props: defaultProps })
    await wrapper.find('#activity-board-select').setValue('b2')
    expect(wrapper.emitted('update:selectedBoardId')?.[0]).toEqual(['b2'])
  })

  it('shows current user info when viewMode is "user"', () => {
    const wrapper = mount(ActivitySelector, {
      props: { ...defaultProps, viewMode: 'user' as const },
    })
    expect(wrapper.text()).toContain('Current user:')
    expect(wrapper.text()).toContain('alice')
    // Board selector should NOT be shown in user mode
    expect(wrapper.find('#activity-board-select').exists()).toBe(false)
  })

  it('shows entity type selector when viewMode is "entity"', () => {
    const wrapper = mount(ActivitySelector, {
      props: { ...defaultProps, viewMode: 'entity' as const },
    })
    const entityTypeSelect = wrapper.find('#activity-entity-type')
    expect(entityTypeSelect.exists()).toBe(true)
    const options = entityTypeSelect.findAll('option')
    // placeholder + Board, Column, Card, Label
    expect(options).toHaveLength(5)
  })

  it('emits update:selectedEntityType when entity type changes', async () => {
    const wrapper = mount(ActivitySelector, {
      props: { ...defaultProps, viewMode: 'entity' as const },
    })
    await wrapper.find('#activity-entity-type').setValue('Card')
    expect(wrapper.emitted('update:selectedEntityType')?.[0]).toEqual(['Card'])
  })

  it('shows limit selector with all options', () => {
    const wrapper = mount(ActivitySelector, { props: defaultProps })
    const limitSelect = wrapper.find('[aria-label="Activity limit"]')
    expect(limitSelect.exists()).toBe(true)
    const options = limitSelect.findAll('option')
    expect(options).toHaveLength(3)
    expect(options.map((o) => o.text())).toEqual(['25', '50', '100'])
  })

  it('emits fetch when Fetch button is clicked', async () => {
    const wrapper = mount(ActivitySelector, { props: defaultProps })
    const fetchBtn = wrapper.findAll('button').find((b) => b.text() === 'Fetch')
    expect(fetchBtn).toBeTruthy()
    await fetchBtn?.trigger('click')
    expect(wrapper.emitted('fetch')).toHaveLength(1)
  })

  it('disables Fetch button when canFetch is false', () => {
    const wrapper = mount(ActivitySelector, {
      props: { ...defaultProps, canFetch: false },
    })
    const fetchBtn = wrapper.findAll('button').find((b) => b.text() === 'Fetch')
    expect(fetchBtn).toBeTruthy()
    expect(fetchBtn?.attributes('disabled')).toBeDefined()
  })

  it('shows loading helper text when loadingEntitySource is true', () => {
    const wrapper = mount(ActivitySelector, {
      props: { ...defaultProps, viewMode: 'entity' as const, loadingEntitySource: true },
    })
    expect(wrapper.text()).toContain('Loading entities for selected board...')
  })

  it('shows copy ID affordance when selectedIdForCopy is set', () => {
    const wrapper = mount(ActivitySelector, {
      props: { ...defaultProps, selectedIdForCopy: 'abc-123', selectedIdLabel: 'Board ID' },
    })
    expect(wrapper.text()).toContain('Board ID')
    expect(wrapper.text()).toContain('abc-123')
    expect(wrapper.text()).toContain('Copy Raw ID')
  })

  it('emits copyId when Copy Raw ID button is clicked', async () => {
    const wrapper = mount(ActivitySelector, {
      props: { ...defaultProps, selectedIdForCopy: 'abc-123', selectedIdLabel: 'Board ID' },
    })
    const copyBtn = wrapper.findAll('button').find((b) => b.text().includes('Copy Raw ID'))
    expect(copyBtn).toBeTruthy()
    await copyBtn?.trigger('click')
    expect(wrapper.emitted('copyId')).toHaveLength(1)
  })
})
