import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import AppShell from '../../components/shell/AppShell.vue'

const mockRouter = {
  push: vi.fn(),
}

const mockRoute = reactive({
  path: '/workspace/boards',
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

function mountShell() {
  return mount(AppShell, {
    global: {
      stubs: {
        RouterView: true,
        Teleport: true,
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

describe('AppShell command palette keyboard model', () => {
  let mountedWrapper: ReturnType<typeof mountShell> | null = null

  beforeEach(() => {
    vi.clearAllMocks()
    mockRoute.path = '/workspace/boards'
  })

  afterEach(() => {
    mountedWrapper?.unmount()
    mountedWrapper = null
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

    expect(mockRouter.push).toHaveBeenCalledWith('/workspace/activity')
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
