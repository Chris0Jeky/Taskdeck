import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import AppShell from '../../components/shell/AppShell.vue'
import type { FeatureFlags } from '../../types/feature-flags'

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
  isEnabled: vi.fn((_flag: keyof FeatureFlags) => true),
}

const mockWorkspace = reactive({
  mode: 'guided' as string,
  updateMode: vi.fn<(mode: 'guided' | 'workbench' | 'agent') => Promise<void>>(),
  inboxBadgeCount: 0,
  reviewBadgeCount: 0,
  hasHomeSummary: false,
  homeLoading: false,
  preferenceLoading: false,
  preferencesHydrated: false,
  hydratePreferences: vi.fn().mockResolvedValue(null),
  fetchHomeSummary: vi.fn().mockResolvedValue(undefined),
  resetForLogout: vi.fn(),
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

const mockPaperTheme = reactive({
  mode: 'off' as 'off' | 'paper' | 'paper-night' | 'auto',
  isOn: false,
  activeClass: null as 'paper' | 'paper-night' | null,
  toggleNight: vi.fn(),
  setMode: vi.fn(),
  apply: vi.fn(),
  enable: vi.fn(),
  disable: vi.fn(),
})

vi.mock('../../store/paperThemeStore', () => ({
  usePaperThemeStore: () => mockPaperTheme,
}))

vi.mock('../../composables/useCaptureQueueSync', () => ({
  useCaptureQueueSync: () => ({ pendingCount: { value: 0 }, syncing: { value: false }, replayQueue: vi.fn(), registerBackgroundSync: vi.fn(), refreshCount: vi.fn() }),
}))

function mountShell(attachTo?: HTMLElement) {
  return mount(AppShell, {
    attachTo,
    global: {
      stubs: {
        RouterView: true,
        Teleport: true,
        CaptureModal: {
          template: `
            <div role="dialog" aria-modal="true" aria-label="Capture modal">
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

function getRenderedNavHrefs(wrapper: ReturnType<typeof mountShell>) {
  return wrapper.findAll('a').map((link) => link.attributes('href'))
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
    mockWorkspace.inboxBadgeCount = 0
    mockWorkspace.reviewBadgeCount = 0
    mockWorkspace.hasHomeSummary = false
    mockWorkspace.homeLoading = false
    mockWorkspace.preferenceLoading = false
    mockWorkspace.preferencesHydrated = false
    mockFeatureFlags.isEnabled = vi.fn((_flag: keyof FeatureFlags) => true)
  })

  afterEach(() => {
    mountedWrapper?.unmount()
    mountedWrapper = null
  })

  it('shows reduced IA sidebar with primary items', async () => {
    mountedWrapper = mountShell()
    const wrapper = mountedWrapper

    // Reduced IA: sidebar shows only primary items
    expect(wrapper.text()).toContain('Today')
    expect(wrapper.text()).toContain('Review')
    expect(wrapper.text()).toContain('Boards')
    expect(wrapper.text()).toContain('Inbox')
    expect(wrapper.text()).toContain('Search')
    expect(wrapper.text()).toContain('Settings')
    // Demoted items no longer appear in sidebar
    expect(wrapper.text()).not.toContain('Workbench Tools')
    expect(wrapper.text()).not.toContain('Activity')
  })

  it('shows same reduced IA sidebar in workbench mode', async () => {
    mockWorkspace.mode = 'workbench'
    mountedWrapper = mountShell()
    const wrapper = mountedWrapper

    // Reduced IA applies across all modes
    expect(wrapper.text()).toContain('Today')
    expect(wrapper.text()).toContain('Boards')
    expect(wrapper.text()).toContain('Search')
    // Demoted items are not in the sidebar regardless of mode
    expect(wrapper.text()).not.toContain('Workbench Tools')
  })

  it('shows sidebarPrimary items with workbenchBypassesFlag in workbench mode even when flags are off', async () => {
    mockWorkspace.mode = 'workbench'
    mockFeatureFlags.isEnabled = vi.fn((_flag: keyof FeatureFlags) => false)
    mountedWrapper = mountShell()
    const wrapper = mountedWrapper
    const navHrefs = getRenderedNavHrefs(wrapper)

    // Review has sidebarPrimary=true and workbenchBypassesFlag=true,
    // so it appears even when its flag is off
    expect(navHrefs).toContain('/workspace/review')
    // Demoted surfaces are no longer in the sidebar nav, regardless of mode/flags
    expect(navHrefs).not.toContain('/workspace/automations/chat')
    expect(navHrefs).not.toContain('/workspace/activity')
    expect(navHrefs).not.toContain('/workspace/ops/cli')
  })

  it('hides feature-flagged surfaces in guided mode when flags are off', async () => {
    mockWorkspace.mode = 'guided'
    const advancedFlagsOff = new Set<keyof FeatureFlags>(['newActivity', 'newOps', 'newAccess', 'newArchive'])
    mockFeatureFlags.isEnabled = vi.fn((flag: keyof FeatureFlags) => !advancedFlagsOff.has(flag))
    mountedWrapper = mountShell()
    const wrapper = mountedWrapper
    const navHrefs = getRenderedNavHrefs(wrapper)
    const text = wrapper.text()

    // Reduced IA: only sidebarPrimary items are shown
    expect(text).toContain('Boards')
    expect(text).toContain('Inbox')
    expect(navHrefs).toContain('/workspace/review')
    // Demoted surfaces never appear in the sidebar nav
    expect(navHrefs).not.toContain('/workspace/activity')
    expect(navHrefs).not.toContain('/workspace/ops/cli')
    expect(navHrefs).not.toContain('/workspace/settings/access')
    expect(navHrefs).not.toContain('/workspace/archive')
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

  it('keeps Review highlighted for the advanced queue route', async () => {
    mockRoute.path = '/workspace/automations/queue'
    mountedWrapper = mountShell()
    const wrapper = mountedWrapper

    const reviewLink = wrapper.findAll('a').find((link) => link.attributes('href') === '/workspace/review')
    expect(reviewLink?.attributes('aria-current')).toBe('page')
    expect(reviewLink?.classes()).toContain('td-nav-item--active')
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

  it.each([
    ['h', '/workspace/home'],
    ['t', '/workspace/today'],
    ['b', '/workspace/boards'],
    ['i', '/workspace/inbox'],
    ['r', '/workspace/review'],
  ])('navigates with the bare %s workspace binding', async (key, path) => {
    mountedWrapper = mountShell()

    window.dispatchEvent(new KeyboardEvent('keydown', { key }))
    await waitForUi()

    expect(mockRouter.push).toHaveBeenCalledWith(path)
  })

  it('navigates to Today through the G T chord', async () => {
    mountedWrapper = mountShell()

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'g' }))
    expect(mockRouter.push).not.toHaveBeenCalled()
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 't' }))
    await waitForUi()

    expect(mockRouter.push).toHaveBeenCalledWith('/workspace/today')
  })

  it('suppresses workspace navigation while a modal owns the keyboard', async () => {
    mountedWrapper = mountShell(document.body)
    const wrapper = mountedWrapper

    window.dispatchEvent(new KeyboardEvent('keydown', {
      key: 'C',
      ctrlKey: true,
      shiftKey: true,
    }))
    await waitForUi()

    const modalButton = wrapper.get('.capture-close')
    const modalButtonElement = modalButton.element as HTMLButtonElement
    modalButtonElement.focus()
    expect(document.activeElement).toBe(modalButtonElement)
    mockRouter.push.mockClear()

    modalButtonElement.dispatchEvent(new KeyboardEvent('keydown', {
      key: 'h',
      bubbles: true,
    }))
    modalButtonElement.dispatchEvent(new KeyboardEvent('keydown', {
      key: 'g',
      bubbles: true,
    }))
    modalButtonElement.dispatchEvent(new KeyboardEvent('keydown', {
      key: 't',
      bubbles: true,
    }))
    await waitForUi()

    expect(mockRouter.push).not.toHaveBeenCalled()
  })

  it.each(['input', 'textarea', 'select', 'contenteditable'])(
    'suppresses bare and chord navigation inside %s targets',
    async (kind) => {
      mountedWrapper = mountShell()
      const target = kind === 'contenteditable'
        ? document.createElement('div')
        : document.createElement(kind)
      if (kind === 'contenteditable') target.setAttribute('contenteditable', 'true')
      document.body.appendChild(target)

      target.dispatchEvent(new KeyboardEvent('keydown', { key: 't', bubbles: true }))
      target.dispatchEvent(new KeyboardEvent('keydown', { key: 'g', bubbles: true }))
      target.dispatchEvent(new KeyboardEvent('keydown', { key: 't', bubbles: true }))
      await waitForUi()

      expect(mockRouter.push).not.toHaveBeenCalled()
      target.remove()
    },
  )

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
    expect(input.attributes('aria-activedescendant')).toBe('td-palette-option-0')

    const listbox = wrapper.get('#td-command-palette-listbox')
    expect(listbox.attributes('role')).toBe('listbox')

    let options = wrapper.findAll('[role="option"]')
    expect(options[0].attributes('aria-selected')).toBe('true')

    await input.trigger('keydown.down')
    await waitForUi()

    options = wrapper.findAll('[role="option"]')
    expect(options[1].attributes('aria-selected')).toBe('true')
    expect(wrapper.get('.td-command-palette__input').attributes('aria-activedescendant')).toBe('td-palette-option-1')
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

  it('shows nav badges when inbox and review have pending items', async () => {
    mockWorkspace.inboxBadgeCount = 3
    mockWorkspace.reviewBadgeCount = 1
    mountedWrapper = mountShell()
    const wrapper = mountedWrapper

    const badges = wrapper.findAll('.td-nav-badge')
    expect(badges.length).toBe(2)

    const badgeTexts = badges.map((b) => b.text())
    expect(badgeTexts).toContain('3')
    expect(badgeTexts).toContain('1')

    const inboxBadge = badges.find((b) => b.text() === '3')
    expect(inboxBadge?.attributes('aria-label')).toBe('Inbox: 3 pending')
    const reviewBadge = badges.find((b) => b.text() === '1')
    expect(reviewBadge?.attributes('aria-label')).toBe('Review: 1 pending')
  })

  it('hides nav badges when counts are zero', async () => {
    mockWorkspace.inboxBadgeCount = 0
    mockWorkspace.reviewBadgeCount = 0
    mountedWrapper = mountShell()
    const wrapper = mountedWrapper

    expect(wrapper.findAll('.td-nav-badge').length).toBe(0)
  })

  it('fetches home summary on mount when authenticated and not already loaded', () => {
    mockWorkspace.hasHomeSummary = false
    mockWorkspace.homeLoading = false
    mountedWrapper = mountShell()

    expect(mockWorkspace.fetchHomeSummary).toHaveBeenCalledOnce()
    expect(mockWorkspace.hydratePreferences).not.toHaveBeenCalled()
  })

  it('falls back to preference hydration when the startup home summary fetch fails', async () => {
    mockWorkspace.hasHomeSummary = false
    mockWorkspace.homeLoading = false
    mockWorkspace.preferencesHydrated = false
    mockWorkspace.preferenceLoading = false
    mockWorkspace.fetchHomeSummary.mockRejectedValueOnce(new Error('summary unavailable'))

    mountedWrapper = mountShell()
    await waitForUi()

    expect(mockWorkspace.fetchHomeSummary).toHaveBeenCalledOnce()
    expect(mockWorkspace.hydratePreferences).toHaveBeenCalledOnce()
  })

  it('skips home summary fetch when already loaded', () => {
    mockWorkspace.hasHomeSummary = true
    mockWorkspace.homeLoading = false
    mountedWrapper = mountShell()

    expect(mockWorkspace.fetchHomeSummary).not.toHaveBeenCalled()
  })

  it('hydrates preferences when home summary is already loaded and preferences are stale', () => {
    mockWorkspace.hasHomeSummary = true
    mockWorkspace.preferencesHydrated = false
    mockWorkspace.preferenceLoading = false
    mountedWrapper = mountShell()

    expect(mockWorkspace.hydratePreferences).toHaveBeenCalledOnce()
  })

  it('skips preference hydration while preferences are already loading', () => {
    mockWorkspace.hasHomeSummary = true
    mockWorkspace.preferencesHydrated = false
    mockWorkspace.preferenceLoading = true
    mountedWrapper = mountShell()

    expect(mockWorkspace.hydratePreferences).not.toHaveBeenCalled()
  })

  it('skips home summary fetch when already loading', () => {
    mockWorkspace.hasHomeSummary = false
    mockWorkspace.homeLoading = true
    mountedWrapper = mountShell()

    expect(mockWorkspace.fetchHomeSummary).not.toHaveBeenCalled()
  })
})
