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

const routerLinkStub = { template: '<a><slot /></a>', props: ['to'] }

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
    mockFeatureFlags.isEnabled = vi.fn(() => true)
    routeMock.path = '/workspace/home'
  })

  it('renders the Taskdeck brand title', () => {
    const wrapper = mountSidebar()
    expect(wrapper.text()).toContain('Taskdeck')
    expect(wrapper.text()).toContain('Precision Mode Active')
  })

  it('renders primary nav items for guided mode', () => {
    const wrapper = mountSidebar()
    expect(wrapper.text()).toContain('Home')
    expect(wrapper.text()).toContain('Today')
    expect(wrapper.text()).toContain('Review')
    expect(wrapper.text()).toContain('Boards')
    expect(wrapper.text()).toContain('Inbox')
  })

  it('shows secondary nav items with Workbench Tools section label in guided mode', () => {
    mockWorkspaceStore.mode = 'guided'
    const wrapper = mountSidebar()
    // In guided mode, items with secondaryModes including 'guided' appear as secondary
    expect(wrapper.text()).toContain('Workbench Tools')
    expect(wrapper.text()).toContain('Views')
    expect(wrapper.text()).toContain('Notifications')
  })

  it('promotes all nav items to primary in workbench mode', () => {
    mockWorkspaceStore.mode = 'workbench'
    const wrapper = mountSidebar()
    // In workbench mode, items with primaryModes=['workbench'] are primary, not secondary
    expect(wrapper.text()).toContain('Metrics')
    expect(wrapper.text()).toContain('Activity')
    expect(wrapper.text()).toContain('Ops')
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
    expect(wrapper.text()).not.toContain('Review')
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
})
