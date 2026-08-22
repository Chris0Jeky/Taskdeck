import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import AppShell from '../../components/shell/AppShell.vue'
// Read through Vite's `?raw` loader rather than node:fs — this project deliberately excludes
// node types (adding them breaks production source), and `?raw` also resolves relative to this
// file instead of the process CWD.
import appShellSource from '../../components/shell/AppShell.vue?raw'

const mockRouter = {
  push: vi.fn(),
}

const mockRoute = reactive({
  path: '/workspace/home',
  matched: [
    { path: '/workspace', name: 'workspace', meta: { breadcrumb: 'Workspace' } },
    { path: '/workspace/home', name: 'workspace-home', meta: { breadcrumb: 'Home' } },
  ] as Array<{ path: string; name?: string; meta?: Record<string, unknown> }>,
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
  inboxBadgeCount: 0,
  reviewBadgeCount: 0,
  hasHomeSummary: true,
  homeLoading: false,
  preferenceLoading: false,
  preferencesHydrated: false,
  hydratePreferences: vi.fn().mockResolvedValue(null),
  fetchHomeSummary: vi.fn().mockResolvedValue(undefined),
  resetForLogout: vi.fn(),
})

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

const mockViewportMode = vi.hoisted(() => ({
  value: 'desktop' as 'desktop' | 'tablet' | 'phone',
}))

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

vi.mock('../../store/paperThemeStore', () => ({
  usePaperThemeStore: () => mockPaperTheme,
}))

vi.mock('../../composables/useViewportMode', async () => {
  const { computed } = await import('vue')
  return { useViewportMode: () => ({ mode: computed(() => mockViewportMode.value) }) }
})

vi.mock('../../composables/useCaptureQueueSync', () => ({
  useCaptureQueueSync: () => ({ pendingCount: { value: 0 }, syncing: { value: 0 }, replayQueue: vi.fn(), registerBackgroundSync: vi.fn(), refreshCount: vi.fn() }),
}))

// Every paper-mode mount below renders the real PaperSidebar, which reads the
// product version through `useProductVersion`. Unstubbed, that fires a genuine
// request at the configured API root (`http://localhost:5000/health/live`) from
// happy-dom — passing quietly on a box with no backend, behaving differently on
// one where the API is up, and repeating on every mount because the failed load
// clears the memo. Stub the transport; this suite is about shell routing.
vi.mock('../../api/versionApi', () => ({
  versionApi: {
    getProductVersion: vi.fn(async () => null),
  },
}))

function mountShell() {
  return mount(AppShell, {
    global: {
      stubs: {
        RouterView: true,
        Teleport: true,
        CaptureModal: { template: '<div />' },
        RouterLink: {
          props: ['to'],
          template: '<a :href="to" :class="$attrs.class" :aria-current="$attrs[`aria-current`]"><slot /></a>',
        },
      },
    },
  })
}

describe('AppShell — paper variant routing', () => {
  let wrapper: ReturnType<typeof mountShell> | null = null

  beforeEach(() => {
    vi.clearAllMocks()
    mockPaperTheme.mode = 'off'
    mockPaperTheme.isOn = false
    mockPaperTheme.activeClass = null
    mockWorkspace.mode = 'guided'
    mockWorkspace.hasHomeSummary = true
    mockWorkspace.homeLoading = false
    mockWorkspace.preferenceLoading = false
    mockWorkspace.preferencesHydrated = false
    mockRoute.path = '/workspace/home'
    mockViewportMode.value = 'desktop'
  })

  afterEach(() => {
    wrapper?.unmount()
    wrapper = null
  })

  it('renders the existing Obsidian shell when paper mode is off', () => {
    wrapper = mountShell()
    // Obsidian shell sidebar uses `.td-sidebar`; topbar uses `.td-topbar`.
    expect(wrapper.find('.td-sidebar').exists()).toBe(true)
    expect(wrapper.find('.td-topbar').exists()).toBe(true)
    // Paper variants must NOT be in the DOM
    expect(wrapper.find('[data-paper-sidebar]').exists()).toBe(false)
    expect(wrapper.find('[data-paper-topbar]').exists()).toBe(false)
    expect(wrapper.find('.td-shell').classes()).not.toContain('td-shell--paper')
  })

  it('renders the Paper variants when paper mode is on, hiding the Obsidian shell', () => {
    mockPaperTheme.mode = 'paper'
    mockPaperTheme.isOn = true
    mockPaperTheme.activeClass = 'paper'
    wrapper = mountShell()

    // Paper variants are present
    expect(wrapper.find('[data-paper-sidebar]').exists()).toBe(true)
    expect(wrapper.find('[data-paper-topbar]').exists()).toBe(true)
    // Obsidian shell parts are NOT present
    expect(wrapper.find('.td-sidebar').exists()).toBe(false)
    expect(wrapper.find('.td-topbar').exists()).toBe(false)
    // Shell wrapper carries the paper modifier class
    expect(wrapper.find('.td-shell').classes()).toContain('td-shell--paper')
  })

  it('wires the Paper top bar palette:open to the existing command palette', async () => {
    mockPaperTheme.mode = 'paper'
    mockPaperTheme.isOn = true
    mockPaperTheme.activeClass = 'paper'
    wrapper = mountShell()

    // Click the Paper ⌘K trigger and expect the Obsidian command palette to open.
    await wrapper.find('.paper-topbar__palette').trigger('click')
    await Promise.resolve()
    expect(wrapper.find('[aria-label="Command palette"]').exists()).toBe(true)
  })

  it('keeps paper navigation items available in the command palette', async () => {
    mockPaperTheme.mode = 'paper'
    mockPaperTheme.isOn = true
    mockPaperTheme.activeClass = 'paper'
    wrapper = mountShell()

    await wrapper.find('.paper-topbar__palette').trigger('click')
    await Promise.resolve()

    const paletteText = wrapper.text()
    expect(paletteText).toContain('Boards')
    expect(paletteText).toContain('Inbox')
    expect(paletteText).toContain('Agents')
    expect(paletteText).toContain('Archive')
    expect(paletteText).toContain('New Capture')
  })

  it('keeps the mobile menu trigger wired in paper mode', async () => {
    mockPaperTheme.mode = 'paper'
    mockPaperTheme.isOn = true
    mockPaperTheme.activeClass = 'paper'
    wrapper = mountShell()

    const hamburger = wrapper.find('.td-mobile-topbar__hamburger')
    expect(hamburger.exists()).toBe(true)

    await hamburger.trigger('click')

    expect(wrapper.find('.paper-sidebar--mobile-open').exists()).toBe(true)
  })

  it('hides the legacy mobile hamburger in Paper phone mode', () => {
    mockPaperTheme.mode = 'paper'
    mockPaperTheme.isOn = true
    mockPaperTheme.activeClass = 'paper'
    mockViewportMode.value = 'phone'

    wrapper = mountShell()

    expect(wrapper.find('.td-mobile-topbar').exists()).toBe(false)
    expect(wrapper.find('.td-mobile-topbar__hamburger').exists()).toBe(false)
    expect(wrapper.find('.td-shell').classes()).toContain('td-shell--paper-phone')
    expect(wrapper.find('[data-paper-bottombar]').exists()).toBe(true)
  })

  it('hides the legacy mobile hamburger in Paper tablet mode', () => {
    mockPaperTheme.mode = 'paper'
    mockPaperTheme.isOn = true
    mockPaperTheme.activeClass = 'paper'
    mockViewportMode.value = 'tablet'

    wrapper = mountShell()

    expect(wrapper.find('.td-mobile-topbar').exists()).toBe(false)
    expect(wrapper.find('.td-mobile-topbar__hamburger').exists()).toBe(false)
    expect(wrapper.find('[data-paper-rail]').exists()).toBe(true)
  })

  it('keeps Paper phone content padded above the fixed bottom bar', () => {
    const source = appShellSource

    expect(source).toContain('.td-shell--paper-phone .td-content')
    expect(source).toContain('56px + var(--paper-safe-bottom')
  })

  it('does not render both sidebars simultaneously when toggling paper mode', () => {
    // Paper on: only Paper sidebar
    mockPaperTheme.mode = 'paper'
    mockPaperTheme.isOn = true
    mockPaperTheme.activeClass = 'paper'
    wrapper = mountShell()
    expect(wrapper.find('[data-paper-sidebar]').exists()).toBe(true)
    expect(wrapper.find('.td-sidebar').exists()).toBe(false)

    wrapper.unmount()

    // Paper off: only Obsidian sidebar
    mockPaperTheme.mode = 'off'
    mockPaperTheme.isOn = false
    mockPaperTheme.activeClass = null
    wrapper = mountShell()
    expect(wrapper.find('.td-sidebar').exists()).toBe(true)
    expect(wrapper.find('[data-paper-sidebar]').exists()).toBe(false)
  })

  it('wires the Paper sidebar open-shortcuts event to the keyboard help overlay', async () => {
    mockPaperTheme.mode = 'paper'
    mockPaperTheme.isOn = true
    mockPaperTheme.activeClass = 'paper'
    wrapper = mountShell()

    // The Shortcuts pseudo-link in the Paper sidebar triggers the overlay
    const shortcutsLink = wrapper.findAll('a.paper-sidebar__item')
      .find((l) => l.text().includes('Shortcuts'))
    expect(shortcutsLink).toBeDefined()
    await shortcutsLink?.trigger('click')
    await wrapper.vm.$nextTick()
    // Verify the overlay is now visible via its prop — not just present in the DOM
    const overlay = wrapper.findComponent({ name: 'PaperShortcutsOverlay' })
    expect(overlay.exists()).toBe(true)
    expect(overlay.props('visible')).toBe(true)
  })

  it('wires the Paper sidebar logout to session store', async () => {
    mockPaperTheme.mode = 'paper'
    mockPaperTheme.isOn = true
    mockPaperTheme.activeClass = 'paper'
    wrapper = mountShell()

    const logoutLink = wrapper.findAll('a.paper-sidebar__item')
      .find((l) => l.text().includes('Logout'))
    await logoutLink?.trigger('click')

    expect(mockSession.logout).toHaveBeenCalledTimes(1)
    expect(mockRouter.push).toHaveBeenCalledWith('/login')
  })

  /**
   * The topbar's own spec can only prove PaperTopBar EMITS `logout`. An emit
   * nobody listens to is exactly the class of defect issue #1932 is about, so
   * pin the other half of the seam here: the shell's `@logout` binding, all the
   * way through to `session.logout()`.
   */
  it('wires the Paper top bar account sign-out to session store', async () => {
    mockPaperTheme.mode = 'paper'
    mockPaperTheme.isOn = true
    mockPaperTheme.activeClass = 'paper'
    wrapper = mountShell()

    await wrapper.find('[data-topbar-action="account"]').trigger('click')

    const menu = wrapper.find('.paper-topbar__menu')
    expect(menu.exists()).toBe(true)
    const signOut = menu
      .findAll('[role="menuitem"]')
      .find((item) => item.text().includes('Sign out'))
    expect(signOut).toBeDefined()

    await signOut?.trigger('click')

    expect(mockSession.logout).toHaveBeenCalledTimes(1)
    expect(mockRouter.push).toHaveBeenCalledWith('/login')
  })
})
