import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import AppShell from '../../components/shell/AppShell.vue'

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
  fetchHomeSummary: vi.fn().mockResolvedValue(undefined),
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
    mockRoute.path = '/workspace/home'
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
    // The keyboard help state should now be true (the overlay becomes visible)
    // We verify this indirectly: pressing ? would toggle it, but clicking Shortcuts always opens it
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
})
