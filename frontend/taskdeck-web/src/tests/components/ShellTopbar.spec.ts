import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import ShellTopbar from '../../components/shell/ShellTopbar.vue'

const mockSessionStore = reactive({
  isAuthenticated: true,
  username: 'alice',
})

const mockWorkspaceStore = reactive({
  mode: 'guided' as string,
  updateMode: vi.fn(),
})

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: () => mockSessionStore,
}))

vi.mock('../../store/workspaceStore', () => ({
  useWorkspaceStore: () => mockWorkspaceStore,
}))

describe('ShellTopbar', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockSessionStore.isAuthenticated = true
    mockSessionStore.username = 'alice'
    mockWorkspaceStore.mode = 'guided'
  })

  it('renders workspace mode selector with all three options', () => {
    const wrapper = mount(ShellTopbar)
    const select = wrapper.find('#workspace-mode-select')
    expect(select.exists()).toBe(true)
    const options = select.findAll('option')
    expect(options).toHaveLength(3)
    expect(options[0].text()).toBe('Guided')
    expect(options[1].text()).toBe('Workbench')
    expect(options[2].text()).toBe('Agent')
  })

  it('shows correct description for guided mode', () => {
    const wrapper = mount(ShellTopbar)
    expect(wrapper.text()).toContain('Keep Home, Review, and board work front and center.')
  })

  it('shows correct description for workbench mode', () => {
    mockWorkspaceStore.mode = 'workbench'
    const wrapper = mount(ShellTopbar)
    expect(wrapper.text()).toContain('Show the full shipped workspace')
  })

  it('calls updateMode when workspace mode is changed', async () => {
    const wrapper = mount(ShellTopbar)
    const select = wrapper.find('#workspace-mode-select')
    await select.setValue('workbench')
    expect(mockWorkspaceStore.updateMode).toHaveBeenCalledWith('workbench')
  })

  it('renders command palette trigger button', () => {
    const wrapper = mount(ShellTopbar)
    const paletteBtn = wrapper.find('.td-topbar__palette-trigger')
    expect(paletteBtn.exists()).toBe(true)
    expect(paletteBtn.text()).toContain('Go anywhere... (Ctrl+K)')
  })

  it('emits open-command-palette when palette trigger is clicked', async () => {
    const wrapper = mount(ShellTopbar)
    const paletteBtn = wrapper.find('.td-topbar__palette-trigger')
    await paletteBtn.trigger('click')
    expect(wrapper.emitted('open-command-palette')).toHaveLength(1)
  })

  it('shows username when authenticated', () => {
    const wrapper = mount(ShellTopbar)
    expect(wrapper.find('.td-topbar__user').text()).toBe('alice')
  })

  it('does not show username when not authenticated', () => {
    mockSessionStore.isAuthenticated = false
    const wrapper = mount(ShellTopbar)
    expect(wrapper.find('.td-topbar__user').exists()).toBe(false)
  })

  it('shows system status indicator', () => {
    const wrapper = mount(ShellTopbar)
    expect(wrapper.find('.td-topbar__status-dot').exists()).toBe(true)
    expect(wrapper.find('.td-topbar__status-label').text()).toBe('System Live')
  })

  it('has accessible label on workspace mode select', () => {
    const wrapper = mount(ShellTopbar)
    const select = wrapper.find('#workspace-mode-select')
    expect(select.attributes('aria-label')).toBe('Workspace mode')
  })
})
