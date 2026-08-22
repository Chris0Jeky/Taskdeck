import { describe, it, expect, beforeEach, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { reactive, ref } from 'vue'
import PaperSidebar from '../../../components/paper/PaperSidebar.vue'
import { versionApi } from '../../../api/versionApi'
import { resetProductVersionForTests } from '../../../composables/useProductVersion'
import type { FeatureFlags } from '../../../types/feature-flags'
import type { ViewportMode } from '../../../composables/useViewportMode'

const mockRoute = reactive({
  path: '/workspace/home',
})

const mockWorkspace = reactive({
  mode: 'guided' as string,
  inboxBadgeCount: 0,
  reviewBadgeCount: 0,
  updateMode: vi.fn(async (_mode: string) => {}),
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

const mockViewportMode = ref<ViewportMode>('desktop')

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

vi.mock('../../../composables/useViewportMode', () => ({
  useViewportMode: () => ({ mode: mockViewportMode }),
}))

// Only the transport is stubbed: the real `useProductVersion` composable runs,
// so these specs exercise the whole sidebar -> composable -> API chain (#1948).
vi.mock('../../../api/versionApi', () => ({
  versionApi: {
    getProductVersion: vi.fn(async () => null),
  },
}))

function mountSidebar() {
  return mount(PaperSidebar, {
    global: {
      stubs: {
        RouterLink: {
          props: ['to'],
          template: '<a :href="to" v-bind="$attrs"><slot /></a>',
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
    mockWorkspace.updateMode = vi.fn(async (_mode: string) => {})
    mockFeatureFlags.isEnabled = vi.fn(() => true)
    mockPaperTheme.mode = 'paper'
    mockPaperTheme.activeClass = 'paper'
    mockViewportMode.value = 'desktop'
    resetProductVersionForTests()
    vi.mocked(versionApi.getProductVersion).mockResolvedValue(null)
    document.body.style.overflow = ''
  })

  it('renders the brand and plain-language review-first eyebrow', () => {
    const wrapper = mountSidebar()
    expect(wrapper.find('.paper-sidebar__brand').text()).toBe('Taskdeck')
    expect(wrapper.text()).toContain('Review before changes')
    expect(wrapper.find('.paper-sidebar__eyebrow-active').text()).toContain('active')
  })

  it('renders the workspace switcher chip with the first letter glyph', () => {
    const wrapper = mountSidebar()
    expect(wrapper.find('.paper-sidebar__workspace-glyph').text()).toBe('S')
    expect(wrapper.find('.paper-sidebar__workspace-name').text()).toContain('Solo Workspace')
  })

  it('renders the three IA groups with primary loop, workbench, and meta items', () => {
    mockWorkspace.mode = 'workbench'
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

  it('hides developer-facing Paper navigation behind one guided Advanced disclosure', async () => {
    const wrapper = mountSidebar()
    const toggle = wrapper.get('[data-testid="paper-guided-advanced-toggle"]')

    expect(toggle.attributes('aria-expanded')).toBe('false')
    expect(wrapper.find('[data-group="workbench"]').text()).toContain('More tools')
    const visiblePaths = wrapper.findAll('a.paper-sidebar__item').map(link => link.attributes('href'))
    for (const path of ['/workspace/agents', '/workspace/metrics', '/workspace/integrations', '/workspace/ops/cli', '/workspace/settings/api-keys']) {
      expect(visiblePaths).not.toContain(path)
    }
    expect(visiblePaths).toContain('/workspace/settings/profile')
    expect(visiblePaths).toContain('/workspace/settings/appearance')

    await toggle.trigger('click')

    expect(toggle.attributes('aria-expanded')).toBe('true')
    for (const label of ['Agents', 'Metrics', 'Cohorts', 'Integrations', 'Ops', 'Endpoints', 'Logs', 'API Keys', 'Dev Tools']) {
      expect(wrapper.find('[data-group="advanced"]').text()).toContain(label)
    }
    expect(wrapper.findAll('[data-group="advanced"] a .paper-sidebar__label').map(label => label.text())).toEqual([
      'Agents', 'Metrics', 'Cohorts', 'Integrations', 'Ops', 'Endpoints', 'Logs', 'API Keys', 'Dev Tools',
    ])
  })

  it('keeps the command palette catalog complete while guided navigation is collapsed', () => {
    const wrapper = mountSidebar()
    const exposed = wrapper.vm as unknown as { availableNavItems: Array<{ id: string }> }

    expect(exposed.availableNavItems.map(item => item.id)).toEqual(expect.arrayContaining([
      'agents', 'metrics', 'integrations', 'ops', 'api-keys',
    ]))
  })

  it('switches from the guided Advanced disclosure to the existing workbench mode', async () => {
    const wrapper = mountSidebar()
    await wrapper.get('[data-testid="paper-guided-advanced-toggle"]').trigger('click')
    await wrapper.get('[data-testid="paper-switch-to-workbench"]').trigger('click')

    expect(mockWorkspace.updateMode).toHaveBeenCalledWith('workbench')
  })

  it.each([
    ['/workspace/metrics/cohorts', '/workspace/metrics', '/workspace/metrics/cohorts'],
    ['/workspace/ops/endpoints', '/workspace/ops/cli', '/workspace/ops/endpoints'],
    ['/workspace/ops/logs', '/workspace/ops/cli', '/workspace/ops/logs'],
  ])('marks only the visible guided child current on %s', async (route, parentPath, childPath) => {
    mockRoute.path = route
    const wrapper = mountSidebar()
    await wrapper.get('[data-testid="paper-guided-advanced-toggle"]').trigger('click')

    expect(wrapper.get(`a[href="${parentPath}"]`).attributes('aria-current')).toBeUndefined()
    expect(wrapper.get(`a[href="${childPath}"]`).attributes('aria-current')).toBe('page')
  })

  it.each([
    ['/workspace/metrics/cohorts', '/workspace/metrics'],
    ['/workspace/ops/endpoints', '/workspace/ops/cli'],
    ['/workspace/ops/logs', '/workspace/ops/cli'],
  ])('keeps the visible workbench parent current on child route %s', (route, parentPath) => {
    mockWorkspace.mode = 'workbench'
    mockRoute.path = route
    const wrapper = mountSidebar()

    expect(wrapper.get(`a[href="${parentPath}"]`).attributes('aria-current')).toBe('page')
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

  it('keeps workbench bypass routes available when their feature flags are disabled', () => {
    mockWorkspace.mode = 'workbench'
    mockFeatureFlags.isEnabled = vi.fn(() => false)
    const wrapper = mountSidebar()

    const hrefs = wrapper.findAll('a.paper-sidebar__item').map((l) => l.attributes('href'))
    expect(hrefs).toContain('/workspace/review')
    expect(hrefs).toContain('/workspace/automations/chat')
    expect(hrefs).toContain('/workspace/activity')
    expect(hrefs).toContain('/workspace/ops/cli')
    expect(hrefs).toContain('/workspace/settings/profile')
  })

  it('calls paperThemeStore.toggleNight() when the theme toggle is clicked', async () => {
    const wrapper = mountSidebar()
    const toggle = wrapper.find('.paper-sidebar__theme-toggle')
    expect(toggle.exists()).toBe(true)
    await toggle.trigger('click')
    expect(mockPaperTheme.toggleNight).toHaveBeenCalledTimes(1)
  })

  // #1948 guard: the footer stamp must be whatever the running backend reports,
  // never a literal in the component. Both cases below assert against the
  // stubbed source of truth, so re-hardcoding a version fails this suite.
  it('renders the live status pill and the backend-reported version in the footer', async () => {
    vi.mocked(versionApi.getProductVersion).mockResolvedValue('9.99.0-guard')

    const wrapper = mountSidebar()
    await flushPromises()

    expect(wrapper.text()).toContain('SYSTEM LIVE')
    expect(wrapper.get('[data-testid="paper-sidebar-version"]').text()).toBe('v9.99.0-guard')
  })

  it('renders no version at all when the source of truth cannot supply one', async () => {
    vi.mocked(versionApi.getProductVersion).mockResolvedValue(null)

    const wrapper = mountSidebar()
    await flushPromises()

    expect(wrapper.text()).toContain('SYSTEM LIVE')
    expect(wrapper.find('[data-testid="paper-sidebar-version"]').exists()).toBe(false)
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

  it('renders the inbox badge with the · prefix when inboxBadgeCount > 0', () => {
    mockWorkspace.inboxBadgeCount = 5
    const wrapper = mountSidebar()
    const badges = wrapper.findAll('.paper-sidebar__badge')
    const inboxBadge = badges.find((b) => b.text().includes('5'))
    expect(inboxBadge?.text()).toMatch(/·\s*5/)
    expect(inboxBadge?.attributes('aria-label')).toBe('Inbox: 5 pending')
  })

  it('renders sidebar groups with data-group attributes for styling hooks', () => {
    const wrapper = mountSidebar()
    expect(wrapper.find('[data-group="primary"]').exists()).toBe(true)
    expect(wrapper.find('[data-group="workbench"]').exists()).toBe(true)
    expect(wrapper.find('[data-group="meta"]').exists()).toBe(true)
  })

  it('renders mono glyphs for sidebar items', () => {
    const wrapper = mountSidebar()
    const glyphs = wrapper.findAll('.paper-sidebar__glyph')
    // Glyphs are single-letter mono characters
    expect(glyphs.length).toBeGreaterThan(0)
    expect(glyphs[0].text()).toHaveLength(1)
  })

  it('applies muted styling to meta group items', () => {
    const wrapper = mountSidebar()
    const metaGroup = wrapper.find('[data-group="meta"]')
    expect(metaGroup.classes()).toContain('paper-sidebar__group--muted')
    const metaItems = metaGroup.findAll('.paper-sidebar__item--muted')
    expect(metaItems.length).toBeGreaterThan(0)
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

  it('renders bottom-bar variant with H/T/R/I glyphs on phone', () => {
    mockViewportMode.value = 'phone'
    const wrapper = mountSidebar()

    expect(wrapper.find('[data-paper-bottombar]').exists()).toBe(true)
    expect(wrapper.find('.paper-sidebar--rail').exists()).toBe(false)

    const glyphs = wrapper.findAll('.paper-bottombar__glyph').map((g) => g.text())
    expect(glyphs).toEqual(['H', 'T', 'R', 'I', '…'])

    const tabs = wrapper.findAll('.paper-bottombar__tab')
    expect(tabs).toHaveLength(5)
  })

  it('renders bottom-bar with ember accent on the active route', () => {
    mockViewportMode.value = 'phone'
    mockRoute.path = '/workspace/review'
    const wrapper = mountSidebar()

    const reviewTab = wrapper.findAll('.paper-bottombar__tab')
      .find((t) => t.attributes('href') === '/workspace/review')
    expect(reviewTab?.classes()).toContain('paper-bottombar__tab--active')

    const homeTab = wrapper.findAll('.paper-bottombar__tab')
      .find((t) => t.attributes('href') === '/workspace/home')
    expect(homeTab?.classes()).not.toContain('paper-bottombar__tab--active')
  })

  it('opens the phone More drawer with ARIA state and closes it with Escape', async () => {
    mockViewportMode.value = 'phone'
    const wrapper = mountSidebar()
    const moreButton = wrapper.get('button[aria-label="More"]')

    expect(moreButton.attributes('aria-expanded')).toBe('false')
    expect(moreButton.attributes('aria-controls')).toBe('paper-phone-more-drawer')

    await moreButton.trigger('click')

    expect(moreButton.attributes('aria-expanded')).toBe('true')
    expect(wrapper.find('[data-paper-phone-drawer]').exists()).toBe(true)
    expect(wrapper.find('#paper-phone-more-drawer').exists()).toBe(true)
    expect(wrapper.text()).toContain('Settings')
    expect(wrapper.text()).toContain('Logout')
    expect(document.body.style.overflow).toBe('hidden')

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }))
    await wrapper.vm.$nextTick()

    expect(wrapper.find('[data-paper-phone-drawer]').exists()).toBe(false)
    expect(moreButton.attributes('aria-expanded')).toBe('false')
    expect(document.body.style.overflow).toBe('')
  })

  it('closes the phone More drawer after route changes and pseudo-actions', async () => {
    mockViewportMode.value = 'phone'
    const wrapper = mountSidebar()
    const moreButton = wrapper.get('button[aria-label="More"]')

    await moreButton.trigger('click')
    mockRoute.path = '/workspace/settings/profile'
    await wrapper.vm.$nextTick()

    expect(wrapper.find('[data-paper-phone-drawer]').exists()).toBe(false)

    await moreButton.trigger('click')
    const shortcutsButton = wrapper.findAll('button.paper-sidebar__item')
      .find((button) => button.text().includes('Shortcuts'))
    await shortcutsButton?.trigger('click')

    expect(wrapper.emitted('open-shortcuts')).toHaveLength(1)
    expect(wrapper.find('[data-paper-phone-drawer]').exists()).toBe(false)
  })

  it('keeps feature-flagged phone tabs hidden when unavailable', () => {
    mockViewportMode.value = 'phone'
    mockFeatureFlags.isEnabled = vi.fn((flag: keyof FeatureFlags) => flag !== 'newAutomation')

    const wrapper = mountSidebar()
    const hrefs = wrapper.findAll('.paper-bottombar__tab').map((tab) => tab.attributes('href'))

    expect(hrefs).not.toContain('/workspace/review')
    expect(hrefs).toContain('/workspace/home')
  })

  it('renders icon-only rail on tablet', () => {
    mockViewportMode.value = 'tablet'
    const wrapper = mountSidebar()

    expect(wrapper.find('[data-paper-rail]').exists()).toBe(true)
    expect(wrapper.find('[data-paper-bottombar]').exists()).toBe(false)
    expect(wrapper.find('.paper-sidebar--rail').exists()).toBe(true)

    expect(wrapper.findAll('.paper-sidebar__label')).toHaveLength(0)

    const glyphs = wrapper.findAll('.paper-sidebar__glyph')
    expect(glyphs.length).toBeGreaterThan(0)
  })

  it('renders rail active-route ember accent on tablet', () => {
    mockViewportMode.value = 'tablet'
    mockRoute.path = '/workspace/boards'
    const wrapper = mountSidebar()

    const boardsLink = wrapper.findAll('a.paper-sidebar__item')
      .find((l) => l.attributes('href') === '/workspace/boards')
    expect(boardsLink?.classes()).toContain('paper-sidebar__item--active')
  })
})
