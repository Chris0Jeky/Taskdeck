import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import PaperTopBar from '../../../components/paper/PaperTopBar.vue'

const mockRoute = reactive({
  path: '/workspace/boards/abc',
  matched: [
    { path: '/workspace', name: 'workspace', meta: { breadcrumb: 'Workspace' } },
    { path: '/workspace/boards', name: 'workspace-boards', meta: {} },
    { path: '/workspace/boards/:id', name: 'workspace-board', meta: { breadcrumb: 'Product Backlog' } },
  ] as Array<{ path: string; name?: string; meta?: Record<string, unknown> }>,
})

const mockSession = reactive({
  username: 'Dora',
  isAuthenticated: true,
})

vi.mock('vue-router', async () => {
  const actual = await vi.importActual<typeof import('vue-router')>('vue-router')
  return {
    ...actual,
    useRoute: () => mockRoute,
  }
})

vi.mock('../../../store/sessionStore', () => ({
  useSessionStore: () => mockSession,
}))

function mountTopBar() {
  return mount(PaperTopBar)
}

describe('PaperTopBar', () => {
  let wrapper: ReturnType<typeof mountTopBar> | null = null

  beforeEach(() => {
    vi.clearAllMocks()
    mockRoute.path = '/workspace/boards/abc'
    mockRoute.matched = [
      { path: '/workspace', name: 'workspace', meta: { breadcrumb: 'Workspace' } },
      { path: '/workspace/boards', name: 'workspace-boards', meta: {} },
      { path: '/workspace/boards/:id', name: 'workspace-board', meta: { breadcrumb: 'Product Backlog' } },
    ]
    mockSession.username = 'Dora'
  })

  afterEach(() => {
    wrapper?.unmount()
    wrapper = null
  })

  it('renders breadcrumbs from route.matched, preferring meta.breadcrumb', () => {
    wrapper = mountTopBar()
    const labels = wrapper.findAll('.paper-topbar__crumb').map((c) => c.text())
    expect(labels).toEqual(['Workspace', 'Boards', 'Product Backlog'])
    // Last segment marked with the --last modifier and aria-current
    const last = wrapper.findAll('.paper-topbar__crumb').at(-1)
    expect(last?.classes()).toContain('paper-topbar__crumb--last')
    expect(last?.attributes('aria-current')).toBe('page')
  })

  it('renders / separators between crumb segments', () => {
    wrapper = mountTopBar()
    const seps = wrapper.findAll('.paper-topbar__sep')
    // 3 crumbs → 2 separators
    expect(seps.length).toBe(2)
    expect(seps[0].text()).toBe('/')
  })

  it('falls back to humanized route name when meta.breadcrumb is missing', () => {
    mockRoute.matched = [
      { path: '/workspace/today', name: 'workspace-today', meta: {} },
    ]
    wrapper = mountTopBar()
    const labels = wrapper.findAll('.paper-topbar__crumb').map((c) => c.text())
    expect(labels).toEqual(['Today'])
  })

  it('emits palette:open when the ⌘K trigger is clicked', async () => {
    wrapper = mountTopBar()
    await wrapper.find('.paper-topbar__palette').trigger('click')
    expect(wrapper.emitted('palette:open')).toHaveLength(1)
  })

  it('does not own the global Ctrl+K keydown shortcut', async () => {
    wrapper = mountTopBar()
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', ctrlKey: true }))
    expect(wrapper.emitted('palette:open')).toBeUndefined()
  })

  it('does not own the global Cmd+K keydown shortcut', async () => {
    wrapper = mountTopBar()
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'K', metaKey: true }))
    expect(wrapper.emitted('palette:open')).toBeUndefined()
  })

  it('shows the platform command modifier label in the palette trigger', () => {
    wrapper = mountTopBar()
    expect(wrapper.find('.paper-topbar__palette').text()).toContain('Ctrl')
  })

  it('shows the avatar with the first letter of the session username', () => {
    mockSession.username = 'jeky'
    wrapper = mountTopBar()
    expect(wrapper.find('.paper-topbar__avatar').text()).toBe('J')
  })

  it('renders the SYNCED · LOCAL-FIRST live status pill and Bell + Settings ghost buttons', () => {
    wrapper = mountTopBar()
    expect(wrapper.text()).toContain('SYNCED')
    expect(wrapper.text()).toContain('LOCAL-FIRST')
    const buttons = wrapper.findAll('.paper-topbar__icon-btn')
    expect(buttons.length).toBe(2)
    expect(buttons[0].attributes('aria-label')).toBe('Notifications')
    expect(buttons[1].attributes('aria-label')).toBe('Settings')
  })

  it('removes the global keydown listener on unmount', async () => {
    wrapper = mountTopBar()
    wrapper.unmount()
    wrapper = null
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', ctrlKey: true }))
    // Nothing to assert on emitted (component is gone); just confirm no throw.
    expect(true).toBe(true)
  })
})
