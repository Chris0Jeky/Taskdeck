import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import ShellSidebar from '../../components/shell/ShellSidebar.vue'

const mockFeatureFlags = reactive({
  isEnabled: vi.fn((_flag: string) => true),
})

const mockWorkspaceStore = reactive({
  mode: 'guided' as string,
  inboxBadgeCount: 0,
  reviewBadgeCount: 0,
  updateMode: vi.fn(async (_mode: string) => {}),
})

vi.mock('../../store/featureFlagStore', () => ({
  useFeatureFlagStore: () => mockFeatureFlags,
}))

vi.mock('../../store/workspaceStore', () => ({
  useWorkspaceStore: () => mockWorkspaceStore,
}))

vi.mock('../../composables/useEscapeStack', () => ({
  registerEscapeHandler: vi.fn(() => vi.fn()),
}))

const routeMock = reactive({
  path: '/workspace/home',
})

vi.mock('vue-router', () => ({
  useRoute: () => routeMock,
}))

const routerLinkStub = { template: '<a :href="to"><slot /></a>', props: ['to'] }

function mountSidebar(overrides?: { isAuthenticated?: boolean; stub?: Record<string, unknown> }) {
  return mount(ShellSidebar, {
    props: { isAuthenticated: overrides?.isAuthenticated ?? true },
    global: { stubs: { 'router-link': overrides?.stub ?? routerLinkStub } },
  })
}

describe('ShellSidebar', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockWorkspaceStore.mode = 'guided'
    mockWorkspaceStore.inboxBadgeCount = 0
    mockWorkspaceStore.reviewBadgeCount = 0
    mockWorkspaceStore.updateMode = vi.fn(async (_mode: string) => {})
    mockFeatureFlags.isEnabled = vi.fn(() => true)
    routeMock.path = '/workspace/home'
  })

  it('renders the Taskdeck brand title', () => {
    const wrapper = mountSidebar()
    expect(wrapper.text()).toContain('Taskdeck')
    expect(wrapper.text()).toContain('Review before changes')
  })

  it('renders reduced IA sidebar items: Today, Review, Boards, Inbox', () => {
    const wrapper = mountSidebar()
    expect(wrapper.text()).toContain('Today')
    expect(wrapper.text()).toContain('Review')
    expect(wrapper.text()).toContain('Boards')
    expect(wrapper.text()).toContain('Inbox')
  })

  it('renders Search button that opens command palette', () => {
    const wrapper = mountSidebar()
    expect(wrapper.text()).toContain('Search')
    // Search button should have Ctrl+K hint
    expect(wrapper.text()).toContain('Ctrl+K')
  })

  it('renders Settings link in sidebar footer area', () => {
    const wrapper = mountSidebar()
    expect(wrapper.text()).toContain('Settings')
  })

  it('renders a visible Appearance link in the footer pointing at the theme settings', () => {
    const wrapper = mountSidebar({
      stub: { template: '<a :data-to="to"><slot /></a>', props: ['to'] },
    })
    const appearanceLink = wrapper.findAll('a').find((a) => a.text().includes('Appearance'))
    expect(appearanceLink).toBeTruthy()
    expect(appearanceLink!.attributes('data-to')).toBe('/workspace/settings/appearance')
  })

  it('keeps the Appearance link visible even when newAuth is disabled (theme is auth-independent)', () => {
    mockFeatureFlags.isEnabled = vi.fn((flag: string) => flag !== 'newAuth')
    const wrapper = mountSidebar()
    expect(wrapper.text()).toContain('Appearance')
    // The Settings (profile) link is gated on newAuth; Appearance is not.
    expect(wrapper.text()).not.toContain('Settings')
  })

  it('does not show demoted items (Metrics, Activity, Ops, etc.) in the sidebar', () => {
    const wrapper = mountSidebar()
    // These items were demoted from sidebar and are now command-palette-only
    expect(wrapper.text()).not.toContain('Metrics')
    expect(wrapper.text()).not.toContain('Activity')
    expect(wrapper.text()).not.toContain('Ops')
    expect(wrapper.text()).not.toContain('Views')
    expect(wrapper.text()).not.toContain('Notifications')
    expect(wrapper.text()).not.toContain('Chat')
    expect(wrapper.text()).not.toContain('Calendar')
    expect(wrapper.text()).not.toContain('Integrations')
  })

  it('does not show Home in the sidebar (Home is not sidebarPrimary)', () => {
    const wrapper = mountSidebar()
    // Home is in navCatalog but not sidebarPrimary, so it should not appear
    // in the reduced sidebar nav (it remains accessible via command palette)
    const sidebarNavItems = wrapper.findAll('.td-sidebar__nav .td-nav-item')
    const homeItem = sidebarNavItems.find((el) => el.text().includes('Home'))
    expect(homeItem).toBeUndefined()
  })

  it('emits open-search when Search button is clicked', async () => {
    const wrapper = mountSidebar()
    const searchBtn = wrapper.findAll('.td-nav-item').find((el) => el.text().includes('Search'))
    expect(searchBtn).toBeTruthy()
    await searchBtn!.trigger('click')
    expect(wrapper.emitted('open-search')).toHaveLength(1)
  })

  it('shows badge count on inbox when inboxBadgeCount > 0', () => {
    mockWorkspaceStore.inboxBadgeCount = 5
    const wrapper = mountSidebar()
    const badges = wrapper.findAll('.td-nav-badge')
    const inboxBadge = badges.find((b) => b.text() === '5')
    expect(inboxBadge).toBeDefined()
  })

  it('shows badge count on review when reviewBadgeCount > 0', () => {
    mockWorkspaceStore.reviewBadgeCount = 3
    const wrapper = mountSidebar()
    const badges = wrapper.findAll('.td-nav-badge')
    const reviewBadge = badges.find((b) => b.text() === '3')
    expect(reviewBadge).toBeDefined()
  })

  it('does not show badges when counts are zero', () => {
    const wrapper = mountSidebar()
    expect(wrapper.findAll('.td-nav-badge')).toHaveLength(0)
  })

  it('shows shortcuts button and emits show-keyboard-help on click', async () => {
    const wrapper = mountSidebar()
    const shortcutsBtn = wrapper.find('.td-nav-item--help')
    expect(shortcutsBtn.exists()).toBe(true)
    expect(shortcutsBtn.text()).toContain('Shortcuts')
    await shortcutsBtn.trigger('click')
    expect(wrapper.emitted('show-keyboard-help')).toHaveLength(1)
  })

  it('shows logout button when authenticated and emits logout on click', async () => {
    const wrapper = mountSidebar()
    const logoutBtn = wrapper.find('.td-nav-item--logout')
    expect(logoutBtn.exists()).toBe(true)
    expect(logoutBtn.attributes('aria-label')).toBe('Log out')
    await logoutBtn.trigger('click')
    expect(wrapper.emitted('logout')).toHaveLength(1)
  })

  it('hides logout button when not authenticated', () => {
    const wrapper = mountSidebar({ isAuthenticated: false })
    expect(wrapper.find('.td-nav-item--logout').exists()).toBe(false)
  })

  it('toggles collapsed state when toggle button is clicked', async () => {
    const wrapper = mountSidebar()
    expect(wrapper.find('.td-sidebar--collapsed').exists()).toBe(false)

    const toggleBtn = wrapper.find('.td-sidebar__toggle')
    await toggleBtn.trigger('click')
    expect(wrapper.find('.td-sidebar--collapsed').exists()).toBe(true)

    // Labels should be hidden when collapsed
    expect(wrapper.find('.td-nav-item__label').exists()).toBe(false)
  })

  it('hides feature-flagged items when flag is disabled', () => {
    mockFeatureFlags.isEnabled = vi.fn((flag: string) => {
      if (flag === 'newAutomation') return false
      return true
    })
    const wrapper = mountSidebar()
    expect(wrapper.findAll('.td-sidebar__nav .td-nav-item').some(item => item.text().includes('Review'))).toBe(false)
  })

  it('hides Settings link when newAuth flag is disabled', () => {
    mockFeatureFlags.isEnabled = vi.fn((flag: string) => {
      if (flag === 'newAuth') return false
      return true
    })
    const wrapper = mountSidebar()
    expect(wrapper.text()).not.toContain('Settings')
  })

  it('shows feature-flagged items in workbench mode even when flag is disabled (workbenchBypassesFlag)', () => {
    mockWorkspaceStore.mode = 'workbench'
    mockFeatureFlags.isEnabled = vi.fn(() => false)
    const wrapper = mountSidebar()
    // Review has workbenchBypassesFlag=true so it should appear even with flag disabled
    expect(wrapper.text()).toContain('Review')
  })

  it('has navigation landmark role', () => {
    const wrapper = mountSidebar()
    expect(wrapper.find('aside').attributes('role')).toBe('navigation')
    expect(wrapper.find('aside').attributes('aria-label')).toBe('Main navigation')
  })

  it('highlights the active route with aria-current', () => {
    routeMock.path = '/workspace/inbox'
    const wrapper = mountSidebar({
      stub: {
        template: '<a :class="{ \'td-nav-item--active\': $attrs.class?.includes(\'td-nav-item--active\') }" :aria-current="$attrs[\'aria-current\']"><slot /></a>',
        props: ['to'],
      },
    })
    // The Inbox nav item should have td-nav-item--active class applied by the component
    const navItems = wrapper.findAll('.td-nav-item')
    const inboxItem = navItems.find((el) => el.text().includes('Inbox'))
    expect(inboxItem).toBeTruthy()
    expect(inboxItem!.classes()).toContain('td-nav-item--active')
  })

  it('exposes availableNavItems via defineExpose for command palette', () => {
    const wrapper = mountSidebar()
    const exposed = (wrapper.vm as unknown as { availableNavItems: Array<{ id: string }> }).availableNavItems
    expect(Array.isArray(exposed)).toBe(true)
    expect(exposed.length).toBeGreaterThan(0)
    expect(exposed.some((item) => item.id === 'home')).toBe(true)
  })

  it('keeps developer destinations command-palette reachable while guided navigation is collapsed', () => {
    const wrapper = mountSidebar()
    const exposed = (wrapper.vm as unknown as { availableNavItems: Array<{ id: string }> }).availableNavItems
    expect(exposed.some((item) => item.id === 'metrics')).toBe(true)
    expect(exposed.some((item) => item.id === 'integrations')).toBe(true)
    expect(exposed.some((item) => item.id === 'ops')).toBe(true)
    expect(exposed.some((item) => item.id === 'api-keys')).toBe(true)
    expect(exposed.some((item) => item.id === 'agents')).toBe(true)
    // Non-developer demoted destinations remain command-palette reachable.
    expect(exposed.some((item) => item.id === 'activity')).toBe(true)
    expect(exposed.some((item) => item.id === 'views')).toBe(true)
    expect(exposed.some((item) => item.id === 'notifications')).toBe(true)
    expect(exposed.some((item) => item.id === 'chat')).toBe(true)
  })

  it('reveals all guided developer destinations behind one accessible Advanced disclosure', async () => {
    const wrapper = mountSidebar()
    const toggle = wrapper.get('[data-testid="guided-advanced-toggle"]')
    expect(toggle.attributes('aria-expanded')).toBe('false')

    await toggle.trigger('click')

    expect(toggle.attributes('aria-expanded')).toBe('true')
    for (const label of ['Agents', 'Metrics', 'Cohorts', 'Integrations', 'Ops', 'Endpoints', 'Logs', 'API Keys', 'Dev Tools']) {
      expect(wrapper.text()).toContain(label)
    }
    expect(wrapper.findAll('#guided-advanced-destinations a .td-nav-item__label').map(label => label.text())).toEqual([
      'Agents', 'Metrics', 'Cohorts', 'Integrations', 'Ops', 'Endpoints', 'Logs', 'API Keys', 'Dev Tools',
    ])
    const exposed = (wrapper.vm as unknown as { availableNavItems: Array<{ id: string }> }).availableNavItems
    expect(exposed.map(item => item.id)).toEqual(expect.arrayContaining([
      'agents', 'metrics', 'integrations', 'ops', 'api-keys',
    ]))
    for (const path of ['/workspace/metrics/cohorts', '/workspace/ops/endpoints', '/workspace/ops/logs', '/workspace/dev-tools']) {
      expect(wrapper.find(`a[href="${path}"]`).exists()).toBe(true)
    }
  })

  it('offers a discoverable switch to workbench from the Advanced disclosure', async () => {
    const wrapper = mountSidebar()
    await wrapper.get('[data-testid="guided-advanced-toggle"]').trigger('click')

    await wrapper.get('[data-testid="switch-to-workbench"]').trigger('click')

    expect(mockWorkspaceStore.updateMode).toHaveBeenCalledWith('workbench')
  })

  it.each([
    ['/workspace/metrics/cohorts', '/workspace/metrics', '/workspace/metrics/cohorts'],
    ['/workspace/ops/endpoints', '/workspace/ops/cli', '/workspace/ops/endpoints'],
    ['/workspace/ops/logs', '/workspace/ops/cli', '/workspace/ops/logs'],
  ])('marks only the visible guided child current on %s', async (route, parentPath, childPath) => {
    routeMock.path = route
    const wrapper = mountSidebar()
    await wrapper.get('[data-testid="guided-advanced-toggle"]').trigger('click')

    expect(wrapper.get(`a[href="${parentPath}"]`).attributes('aria-current')).toBeUndefined()
    expect(wrapper.get(`a[href="${childPath}"]`).attributes('aria-current')).toBe('page')
  })

  it.each(['workbench', 'agent'])('leaves the existing %s catalog unchanged without guided disclosure', (mode) => {
    mockWorkspaceStore.mode = mode
    const wrapper = mountSidebar()
    expect(wrapper.find('[data-testid="guided-advanced-toggle"]').exists()).toBe(false)
    const exposed = (wrapper.vm as unknown as { availableNavItems: Array<{ id: string }> }).availableNavItems
    expect(exposed.some(item => item.id === 'metrics')).toBe(true)
    expect(exposed.some(item => item.id === 'ops')).toBe(true)
    expect(exposed.some(item => item.id === 'dev-tools')).toBe(false)
  })
})
