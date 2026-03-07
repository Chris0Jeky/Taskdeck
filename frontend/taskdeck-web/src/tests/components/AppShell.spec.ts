import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import AppShell from '../../components/shell/AppShell.vue'

const mockRouter = {
  push: vi.fn(),
}

const mockRoute = reactive({
  path: '/workspace/home',
})

const mockSession = {
  isAuthenticated: true,
  username: 'test-user',
  logout: vi.fn(),
}

const mockFeatureFlags = {
  flags: {
    newShell: true,
    newAuth: true,
    newAccess: true,
    newActivity: true,
    newOps: true,
    newAutomation: true,
    newArchive: true,
  },
  isEnabled: vi.fn(() => true),
}

const mockWorkspace = reactive({
  mode: 'guided' as string,
  updateMode: vi.fn<(mode: 'guided' | 'workbench' | 'agent') => Promise<void>>(),
})

vi.mock('vue-router', () => ({
  useRouter: () => mockRouter,
  useRoute: () => mockRoute,
}))

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: () => mockSession,
}))

vi.mock('../../store/featureFlagStore', () => ({
  useFeatureFlagStore: () => mockFeatureFlags,
}))

vi.mock('../../store/workspaceStore', () => ({
  useWorkspaceStore: () => mockWorkspace,
}))

function mountShell() {
  return mount(AppShell, {
    global: {
      stubs: {
        RouterView: true,
        Teleport: true,
        CaptureModal: {
          template: `
            <div aria-label="Capture modal">
              <button class="capture-close" @click="$emit('close')">Close</button>
              <button class="capture-created" @click="$emit('created', 'capture-1')">Created</button>
            </div>
          `,
        },
        RouterLink: {
          props: ['to'],
          template: '<a :href="to"><slot /></a>',
        },
      },
    },
  })
}

async function waitForUi() {
  await Promise.resolve()
  await Promise.resolve()
}

describe('AppShell workspace navigation and command palette', () => {
  let mountedWrapper: ReturnType<typeof mountShell> | null = null

  beforeEach(() => {
    vi.clearAllMocks()
    mockRoute.path = '/workspace/home'
    mockWorkspace.mode = 'guided'
    mockWorkspace.updateMode.mockResolvedValue(undefined)
  })

  afterEach(() => {
    mountedWrapper?.unmount()
    mountedWrapper = null
  })

  it('shows guided navigation with workbench tools separated', async () => {
    mountedWrapper = mountShell()
    const wrapper = mountedWrapper

    expect(wrapper.text()).toContain('Home')
    expect(wrapper.text()).toContain('Review')
    expect(wrapper.text()).toContain('Boards')
    expect(wrapper.text()).toContain('Workbench Tools')
    expect(wrapper.text()).toContain('Activity')
  })

  it('shows expanded flat navigation in workbench mode', async () => {
    mockWorkspace.mode = 'workbench'
    mountedWrapper = mountShell()
    const wrapper = mountedWrapper

    expect(wrapper.text()).toContain('Home')
    expect(wrapper.text()).toContain('Activity')
    expect(wrapper.text()).not.toContain('Workbench Tools')
  })

  it('updates workspace mode from the selector', async () => {
    mountedWrapper = mountShell()
    const wrapper = mountedWrapper

    await wrapper.get('[aria-label="Workspace mode"]').setValue('agent')

    expect(mockWorkspace.updateMode).toHaveBeenCalledWith('agent')
  })

  it('ignores unsupported workspace mode values and falls back to guided copy', async () => {
    mockWorkspace.mode = 'unsupported'
    mountedWrapper = mountShell()
    const wrapper = mountedWrapper

    expect(wrapper.text()).toContain('Keep Home, Review, and board work front and center.')

    await wrapper.get('[aria-label="Workspace mode"]').setValue('unsupported')

    expect(mockWorkspace.updateMode).not.toHaveBeenCalled()
  })

  it('toggles command palette with Ctrl+K and closes with Escape', async () => {
    mountedWrapper = mountShell()
    const wrapper = mountedWrapper

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', ctrlKey: true }))
    await waitForUi()
    expect(wrapper.find('[aria-label="Command palette"]').exists()).toBe(true)

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', ctrlKey: true }))
    await waitForUi()
    expect(wrapper.find('[aria-label="Command palette"]').exists()).toBe(false)

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', ctrlKey: true }))
    await waitForUi()
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    await waitForUi()
    expect(wrapper.find('[aria-label="Command palette"]').exists()).toBe(false)
  })

  it('navigates commands with arrows and activates selected command with Enter', async () => {
    mountedWrapper = mountShell()
    const wrapper = mountedWrapper

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', ctrlKey: true }))
    await waitForUi()

    const input = wrapper.get('.td-command-palette__input')
    await input.trigger('keydown.down')
    await input.trigger('keydown.down')
    await input.trigger('keydown.enter')
    await waitForUi()

    expect(mockRouter.push).toHaveBeenCalledWith('/workspace/boards')
    expect(wrapper.find('[aria-label="Command palette"]').exists()).toBe(false)
  })

  it('activates filtered command with Enter', async () => {
    mountedWrapper = mountShell()
    const wrapper = mountedWrapper

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', ctrlKey: true }))
    await waitForUi()

    const input = wrapper.get('.td-command-palette__input')
    await input.setValue('arch')
    await waitForUi()
    await input.trigger('keydown.enter')
    await waitForUi()

    expect(mockRouter.push).toHaveBeenCalledWith('/workspace/archive')
  })

  it('supports notification route from command palette filtering', async () => {
    mountedWrapper = mountShell()
    const wrapper = mountedWrapper

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', ctrlKey: true }))
    await waitForUi()

    const input = wrapper.get('.td-command-palette__input')
    await input.setValue('noti')
    await waitForUi()
    await input.trigger('keydown.enter')
    await waitForUi()

    expect(mockRouter.push).toHaveBeenCalledWith('/workspace/notifications')
  })

  it('supports inbox route from command palette filtering', async () => {
    mountedWrapper = mountShell()
    const wrapper = mountedWrapper

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', ctrlKey: true }))
    await waitForUi()

    const input = wrapper.get('.td-command-palette__input')
    await input.setValue('inbox')
    await waitForUi()
    await input.trigger('keydown.enter')
    await waitForUi()

    expect(mockRouter.push).toHaveBeenCalledWith('/workspace/inbox')
  })

  it('opens capture modal from command palette action and routes to inbox on created', async () => {
    mountedWrapper = mountShell()
    const wrapper = mountedWrapper

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', ctrlKey: true }))
    await waitForUi()

    const input = wrapper.get('.td-command-palette__input')
    await input.setValue('new capture')
    await waitForUi()
    await input.trigger('keydown.enter')
    await waitForUi()

    expect(wrapper.find('[aria-label="Capture modal"]').exists()).toBe(true)
    expect(mockRouter.push).not.toHaveBeenCalledWith('/workspace/capture')

    await wrapper.get('.capture-created').trigger('click')
    await waitForUi()

    expect(mockRouter.push).toHaveBeenCalledWith('/workspace/inbox')
  })

  it('opens capture modal with Ctrl+Shift+C', async () => {
    mountedWrapper = mountShell()
    const wrapper = mountedWrapper

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'C', ctrlKey: true, shiftKey: true }))
    await waitForUi()

    expect(wrapper.find('[aria-label="Capture modal"]').exists()).toBe(true)
  })

  it('does not fire global shortcuts while the workspace mode select is focused', async () => {
    mountedWrapper = mountShell()
    const wrapper = mountedWrapper
    const modeSelect = wrapper.get('[aria-label="Workspace mode"]')

    modeSelect.element.dispatchEvent(new KeyboardEvent('keydown', {
      key: 'C',
      ctrlKey: true,
      shiftKey: true,
      bubbles: true,
    }))
    modeSelect.element.dispatchEvent(new KeyboardEvent('keydown', {
      key: '?',
      bubbles: true,
    }))
    await waitForUi()

    expect(wrapper.find('[aria-label="Capture modal"]').exists()).toBe(false)
    expect(wrapper.find('[aria-label="Keyboard shortcuts"]').exists()).toBe(false)
  })

  it('exposes listbox option accessibility state for keyboard selection', async () => {
    mountedWrapper = mountShell()
    const wrapper = mountedWrapper

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', ctrlKey: true }))
    await waitForUi()

    const input = wrapper.get('.td-command-palette__input')
    expect(input.attributes('role')).toBe('combobox')
    expect(input.attributes('aria-controls')).toBe('td-command-palette-listbox')
    expect(input.attributes('aria-activedescendant')).toBe('td-command-option-0')

    const listbox = wrapper.get('#td-command-palette-listbox')
    expect(listbox.attributes('role')).toBe('listbox')

    let options = wrapper.findAll('[role="option"]')
    expect(options[0].attributes('aria-selected')).toBe('true')

    await input.trigger('keydown.down')
    await waitForUi()

    options = wrapper.findAll('[role="option"]')
    expect(options[1].attributes('aria-selected')).toBe('true')
    expect(wrapper.get('.td-command-palette__input').attributes('aria-activedescendant')).toBe('td-command-option-1')
  })

  it('closes only the top-most escape surface first', async () => {
    mountedWrapper = mountShell()
    const wrapper = mountedWrapper

    window.dispatchEvent(new KeyboardEvent('keydown', { key: '?' }))
    await waitForUi()
    expect(wrapper.find('[aria-label="Keyboard shortcuts"]').exists()).toBe(true)

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', ctrlKey: true }))
    await waitForUi()
    expect(wrapper.find('[aria-label="Command palette"]').exists()).toBe(true)

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    await waitForUi()
    expect(wrapper.find('[aria-label="Command palette"]').exists()).toBe(false)
    expect(wrapper.find('[aria-label="Keyboard shortcuts"]').exists()).toBe(true)

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    await waitForUi()
    expect(wrapper.find('[aria-label="Keyboard shortcuts"]').exists()).toBe(false)
  })
})
