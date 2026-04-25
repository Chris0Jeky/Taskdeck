import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import PaperSidebar from '../../../components/paper/PaperSidebar.vue'
import type { FeatureFlags } from '../../../types/feature-flags'

const mockRoute = reactive({
  path: '/workspace/home',
})

const mockWorkspace = reactive({
  mode: 'guided' as string,
  inboxBadgeCount: 0,
  reviewBadgeCount: 0,
})

const mockFeatureFlags = {
  isEnabled: vi.fn<(flag: keyof FeatureFlags) => boolean>(() => true),
}

const mockPaperTheme = reactive({
  mode: 'paper' as 'off' | 'paper' | 'paper-night' | 'auto',
  isOn: true,
  activeClass: 'paper' as 'paper' | 'paper-night' | null,
  toggleNight: vi.fn(),
})

vi.mock('vue-router', () => ({
  useRoute: () => mockRoute,
}))

vi.mock('../../../store/workspaceStore', () => ({
  useWorkspaceStore: () => mockWorkspace,
}))

vi.mock('../../../store/featureFlagStore', () => ({
  useFeatureFlagStore: () => mockFeatureFlags,
}))

vi.mock('../../../store/paperThemeStore', () => ({
  usePaperThemeStore: () => mockPaperTheme,
}))

function mountSidebar() {
  return mount(PaperSidebar, {
    global: {
      stubs: {
        RouterLink: {
          props: ['to'],
          template: '<a :href="to" :class="$attrs.class" :aria-current="$attrs[`aria-current`]"><slot /></a>',
        },
      },
    },
  })
}

describe('PaperSidebar', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockRoute.path = '/workspace/home'
    mockWorkspace.mode = 'guided'
    mockWorkspace.inboxBadgeCount = 0
    mockWorkspace.reviewBadgeCount = 0
    mockFeatureFlags.isEnabled = vi.fn(() => true)
    mockPaperTheme.mode = 'paper'
    mockPaperTheme.activeClass = 'paper'
  })

  it('renders the brand and Precision Mode eyebrow with the active accent', () => {
    const wrapper = mountSidebar()
    expect(wrapper.find('.paper-sidebar__brand').text()).toBe('Taskdeck')
    expect(wrapper.text()).toContain('Precision Mode')
    expect(wrapper.find('.paper-sidebar__eyebrow-active').text()).toContain('active')
  })

  it('renders the workspace switcher chip with the first letter glyph', () => {
    const wrapper = mountSidebar()
    expect(wrapper.find('.paper-sidebar__workspace-glyph').text()).toBe('S')
    expect(wrapper.find('.paper-sidebar__workspace-name').text()).toContain('Solo Workspace')
  })

  it('renders the three IA groups with primary loop, workbench, and meta items', () => {
    const wrapper = mountSidebar()
    const text = wrapper.text()
    // Primary loop
    expect(text).toContain('Primary loop')
    expect(text).toContain('Home')
    expect(text).toContain('Today')
    expect(text).toContain('Review')
    expect(text).toContain('Boards')
    expect(text).toContain('Inbox')
    // Workbench tools
    expect(text).toContain('Workbench tools')
    expect(text).toContain('Views')
    expect(text).toContain('Notifications')
    expect(text).toContain('Chat')
    expect(text).toContain('Calendar')
    expect(text).toContain('Metrics')
    expect(text).toContain('Integrations')
    expect(text).toContain('Activity')
    expect(text).toContain('Ops')
    // Meta
    expect(text).toContain('Settings')
    expect(text).toContain('API Keys')
    expect(text).toContain('Preferences')
    expect(text).toContain('Shortcuts')
    expect(text).toContain('Logout')
  })

  it('marks the active item with the ember class and aria-current', () => {
    mockRoute.path = '/workspace/boards'
    const wrapper = mountSidebar()
    const links = wrapper.findAll('a.paper-sidebar__item')
    const boardsLink = links.find((l) => l.attributes('href') === '/workspace/boards')
    expect(boardsLink?.classes()).toContain('paper-sidebar__item--active')
    expect(boardsLink?.attributes('aria-current')).toBe('page')
    // non-active items should not get the active class
    const homeLink = links.find((l) => l.attributes('href') === '/workspace/home')
    expect(homeLink?.classes()).not.toContain('paper-sidebar__item--active')
  })

  it('renders the review badge with the · prefix when reviewBadgeCount > 0', () => {
    mockWorkspace.reviewBadgeCount = 3
    const wrapper = mountSidebar()
    const badges = wrapper.findAll('.paper-sidebar__badge')
    expect(badges.length).toBeGreaterThan(0)
    const reviewBadge = badges.find((b) => b.text().includes('3'))
    expect(reviewBadge?.text()).toMatch(/·\s*3/)
    expect(reviewBadge?.attributes('aria-label')).toBe('Review: 3 pending')
  })

  it('hides badges when count is zero', () => {
    mockWorkspace.reviewBadgeCount = 0
    mockWorkspace.inboxBadgeCount = 0
    const wrapper = mountSidebar()
    expect(wrapper.findAll('.paper-sidebar__badge').length).toBe(0)
  })

  it('keeps Review highlighted on the automations queue route', () => {
    mockRoute.path = '/workspace/automations/queue'
    const wrapper = mountSidebar()
    const reviewLink = wrapper.findAll('a.paper-sidebar__item')
      .find((l) => l.attributes('href') === '/workspace/review')
    expect(reviewLink?.classes()).toContain('paper-sidebar__item--active')
  })

  it('filters out items whose feature flag is disabled', () => {
    mockFeatureFlags.isEnabled = vi.fn((flag: keyof FeatureFlags) => flag !== 'newOps' && flag !== 'newActivity')
    const wrapper = mountSidebar()
    const hrefs = wrapper.findAll('a.paper-sidebar__item').map((l) => l.attributes('href'))
    expect(hrefs).not.toContain('/workspace/ops/cli')
    expect(hrefs).not.toContain('/workspace/activity')
    // Items without flags should still be rendered
    expect(hrefs).toContain('/workspace/boards')
  })

  it('calls paperThemeStore.toggleNight() when the theme toggle is clicked', async () => {
    const wrapper = mountSidebar()
    const toggle = wrapper.find('.paper-sidebar__theme-toggle')
    expect(toggle.exists()).toBe(true)
    await toggle.trigger('click')
    expect(mockPaperTheme.toggleNight).toHaveBeenCalledTimes(1)
  })

  it('renders the live status pill and version in the footer', () => {
    const wrapper = mountSidebar()
    expect(wrapper.text()).toContain('SYSTEM LIVE')
    expect(wrapper.text()).toContain('v0.7.2')
  })

  it('emits logout when the meta Logout pseudo-link is clicked', async () => {
    const wrapper = mountSidebar()
    const logoutLink = wrapper.findAll('a.paper-sidebar__item')
      .find((l) => l.text().includes('Logout'))
    await logoutLink?.trigger('click')
    expect(wrapper.emitted('logout')).toHaveLength(1)
  })

  it('emits open-shortcuts when the meta Shortcuts pseudo-link is clicked', async () => {
    const wrapper = mountSidebar()
    const shortcutsLink = wrapper.findAll('a.paper-sidebar__item')
      .find((l) => l.text().includes('Shortcuts'))
    await shortcutsLink?.trigger('click')
    expect(wrapper.emitted('open-shortcuts')).toHaveLength(1)
  })

  it('does not mark prefix-matched sibling routes as active', () => {
    mockRoute.path = '/workspace/boards-archive'
    const wrapper = mountSidebar()
    const boardsLink = wrapper.findAll('a.paper-sidebar__item')
      .find((l) => l.attributes('href') === '/workspace/boards')

    expect(boardsLink?.classes()).not.toContain('paper-sidebar__item--active')
    expect(boardsLink?.attributes('aria-current')).toBeUndefined()
  })

  it('exposes command-palette navigation items with icon fields', () => {
    const wrapper = mountSidebar()
    const exposed = wrapper.vm as unknown as {
      availableNavItems: Array<{ label: string; icon: string; path: string; keywords?: string }>
    }

    expect(exposed.availableNavItems).toEqual(
      expect.arrayContaining([
        expect.objectContaining({ label: 'Boards', icon: 'B', path: '/workspace/boards' }),
        expect.objectContaining({ label: 'Inbox', icon: 'I', path: '/workspace/inbox' }),
        expect.objectContaining({ label: 'Agents', icon: 'G', path: '/workspace/agents' }),
        expect.objectContaining({ label: 'Access', icon: 'A', path: '/workspace/settings/access' }),
        expect.objectContaining({ label: 'Archive', icon: 'Z', path: '/workspace/archive' }),
      ]),
    )
    expect(exposed.availableNavItems.some((item) => item.path.startsWith('#'))).toBe(false)
  })

  it('exposes and closes the mobile menu controls', async () => {
    const wrapper = mountSidebar()
    const exposed = wrapper.vm as unknown as {
      mobileOpen: boolean
      toggleMobileMenu: () => void
    }

    exposed.toggleMobileMenu()
    await wrapper.vm.$nextTick()

    expect(exposed.mobileOpen).toBe(true)
    expect(wrapper.find('.paper-sidebar--mobile-open').exists()).toBe(true)

    const boardsLink = wrapper.findAll('a.paper-sidebar__item')
      .find((l) => l.attributes('href') === '/workspace/boards')
    await boardsLink?.trigger('click')

    expect(exposed.mobileOpen).toBe(false)
  })
})
