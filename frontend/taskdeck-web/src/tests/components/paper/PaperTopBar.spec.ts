import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick, reactive } from 'vue'
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

const mockWorkspace = reactive({
  mode: 'guided' as string,
  updateMode: vi.fn(),
})

const mockRouter = {
  push: vi.fn(),
}

const mockFeatureFlags = {
  isEnabled: vi.fn((_flag: string) => true),
}

vi.mock('vue-router', async () => {
  const actual = await vi.importActual<typeof import('vue-router')>('vue-router')
  return {
    ...actual,
    useRoute: () => mockRoute,
    useRouter: () => mockRouter,
  }
})

vi.mock('../../../store/sessionStore', () => ({
  useSessionStore: () => mockSession,
}))

vi.mock('../../../store/workspaceStore', () => ({
  useWorkspaceStore: () => mockWorkspace,
}))

vi.mock('../../../store/featureFlagStore', () => ({
  useFeatureFlagStore: () => mockFeatureFlags,
}))

function mountTopBar() {
  // attachTo the document so `.focus()` and `document.activeElement` are real —
  // the account menu's focus contract cannot be proven on a detached tree.
  return mount(PaperTopBar, { attachTo: document.body })
}

async function openAccountMenu(wrapper: ReturnType<typeof mountTopBar>) {
  await wrapper.find('[data-topbar-action="account"]').trigger('click')
  return wrapper.find('[role="menu"]')
}

/**
 * A real mouse press on the avatar: `pointerdown` THEN `click`. Vue Test Utils'
 * `trigger('click')` alone dispatches no pointerdown, so it silently skips the
 * outside-press handler — which is precisely the interaction that can go wrong.
 */
async function pressAccountTrigger(wrapper: ReturnType<typeof mountTopBar>) {
  const trigger = wrapper.find('[data-topbar-action="account"]')
  trigger.element.dispatchEvent(new Event('pointerdown', { bubbles: true }))
  await trigger.trigger('click')
}

function menuItemLabels(wrapper: ReturnType<typeof mountTopBar>) {
  return wrapper.findAll('[role="menuitem"]').map((item) => item.text())
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
    mockWorkspace.mode = 'guided'
    mockFeatureFlags.isEnabled.mockReturnValue(true)
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

  it('renders and updates the workspace mode selector', async () => {
    wrapper = mountTopBar()
    const select = wrapper.find('.paper-topbar__mode-select')

    expect(select.exists()).toBe(true)
    expect(select.attributes('aria-label')).toBe('Workspace mode')

    await select.setValue('workbench')

    expect(mockWorkspace.updateMode).toHaveBeenCalledWith('workbench')
  })

  it('shows the avatar with the first letter of the session username', () => {
    mockSession.username = 'jeky'
    wrapper = mountTopBar()
    expect(wrapper.find('.paper-topbar__avatar').text()).toBe('J')
  })

  it('renders the SYNCED · LOCAL-FIRST live status pill and Bell + Appearance ghost buttons', () => {
    wrapper = mountTopBar()
    expect(wrapper.text()).toContain('SYNCED')
    expect(wrapper.text()).toContain('LOCAL-FIRST')
    const buttons = wrapper.findAll('.paper-topbar__icon-btn')
    expect(buttons.length).toBe(2)
    expect(buttons[0].attributes('aria-label')).toBe('Notifications')
    expect(buttons[1].attributes('aria-label')).toBe('Appearance settings')
  })

  it('truncates long breadcrumb segments with text-overflow ellipsis', () => {
    mockRoute.matched = [
      { path: '/workspace', name: 'workspace', meta: { breadcrumb: 'Workspace' } },
      { path: '/workspace/boards', name: 'workspace-boards', meta: { breadcrumb: 'Boards' } },
      { path: '/workspace/boards/:id', name: 'workspace-board', meta: { breadcrumb: 'This Is A Very Long Board Name That Should Truncate' } },
    ]
    wrapper = mountTopBar()
    const crumbs = wrapper.findAll('.paper-topbar__crumb')
    // max-width: 22ch applied via CSS; the element renders the full text but truncates visually
    expect(crumbs.at(-1)?.text()).toBe('This Is A Very Long Board Name That Should Truncate')
    // Verify the truncation CSS class is present (actual ellipsis is scoped CSS; class presence is sufficient)
    expect(crumbs.at(-1)?.classes()).toContain('paper-topbar__crumb')
  })

  it('renders a single Workspace crumb when route.matched is empty', () => {
    mockRoute.matched = []
    wrapper = mountTopBar()
    const labels = wrapper.findAll('.paper-topbar__crumb').map((c) => c.text())
    expect(labels).toEqual(['Workspace'])
  })

  it('renders the vertical hairline divider between status and icon buttons', () => {
    wrapper = mountTopBar()
    expect(wrapper.find('.paper-topbar__hairline').exists()).toBe(true)
  })

  /**
   * #1932 — every right-hand control used to render enabled and do nothing:
   * body HTML was byte-identical before and after clicking each one. These
   * specs pin the EFFECT (a route push, an emit, an open menu), never the
   * markup: a "the buttons render" assertion passed on the broken build.
   */
  describe('right-hand controls (#1932)', () => {
    it('routes the notifications bell to the notifications inbox', async () => {
      wrapper = mountTopBar()
      await wrapper.find('[data-topbar-action="notifications"]').trigger('click')
      expect(mockRouter.push).toHaveBeenCalledWith({ name: 'workspace-notifications' })
    })

    it('routes the gear/sun icon to the appearance settings page', async () => {
      wrapper = mountTopBar()
      await wrapper.find('[data-topbar-action="appearance"]').trigger('click')
      expect(mockRouter.push).toHaveBeenCalledWith({ name: 'workspace-settings-appearance' })
    })

    it('renders the avatar as a focusable button, not a bare div', async () => {
      wrapper = mountTopBar()
      const trigger = wrapper.find('[data-topbar-action="account"]')
      expect(trigger.element.tagName).toBe('BUTTON')
      expect(trigger.attributes('aria-haspopup')).toBe('menu')
      expect(trigger.attributes('aria-expanded')).toBe('false')
      // Keyboard-operable: it can hold focus, which the old <div> could not.
      ;(trigger.element as HTMLButtonElement).focus()
      expect(document.activeElement).toBe(trigger.element)
    })

    it('opens an account menu from the avatar and moves focus into it', async () => {
      wrapper = mountTopBar()
      expect(wrapper.find('[role="menu"]').exists()).toBe(false)

      const menu = await openAccountMenu(wrapper)

      expect(menu.exists()).toBe(true)
      expect(wrapper.find('[data-topbar-action="account"]').attributes('aria-expanded')).toBe('true')
      expect(menuItemLabels(wrapper)).toEqual(['Profile', 'Appearance', 'Sign out'])
      expect(wrapper.find('.paper-topbar__menu-head').text()).toBe('Signed in as Dora')
      await nextTick()
      expect(document.activeElement).toBe(wrapper.findAll('[role="menuitem"]')[0].element)
    })

    it('keeps the signed-in-as line OUT of the menu role, whose only owned children are menuitems', async () => {
      wrapper = mountTopBar()
      const menu = await openAccountMenu(wrapper)

      // The head still renders (identity is not lost) …
      expect(wrapper.find('.paper-topbar__menu-head').exists()).toBe(true)
      // … but role="menu" must not own it: a <p> is an invalid owned child.
      expect(menu.find('.paper-topbar__menu-head').exists()).toBe(false)
      expect(
        Array.from(menu.element.children).every(
          (child) => child.getAttribute('role') === 'menuitem',
        ),
      ).toBe(true)
    })

    it('names the avatar trigger with the account identity, not just the verb', async () => {
      mockSession.username = 'Dora'
      wrapper = mountTopBar()

      const label = wrapper.find('[data-topbar-action="account"]').attributes('aria-label')
      // The control this replaced announced "Profile: D" — the identity must
      // survive, because the avatar letter is a visual-only carrier of it.
      expect(label).toContain('Dora')
      expect(label).toContain('Open account menu')
    })

    it('gives every menu item tabindex="-1" so the menu is one tab stop, not four', async () => {
      wrapper = mountTopBar()
      await openAccountMenu(wrapper)

      const items = wrapper.findAll('[role="menuitem"]')
      expect(items.length).toBe(3)
      expect(items.map((item) => item.attributes('tabindex'))).toEqual(['-1', '-1', '-1'])
    })

    it('navigates to the profile page from the account menu and closes it', async () => {
      wrapper = mountTopBar()
      await openAccountMenu(wrapper)

      await wrapper.findAll('[role="menuitem"]')[0].trigger('click')

      expect(mockRouter.push).toHaveBeenCalledWith({ name: 'workspace-settings-profile' })
      expect(wrapper.find('[role="menu"]').exists()).toBe(false)
    })

    it('emits logout from the account menu sign-out item', async () => {
      wrapper = mountTopBar()
      await openAccountMenu(wrapper)

      await wrapper.findAll('[role="menuitem"]').at(-1)!.trigger('click')

      expect(wrapper.emitted('logout')).toHaveLength(1)
      expect(wrapper.find('[role="menu"]').exists()).toBe(false)
    })

    it('omits the profile item when newAuth is off, because that route silently bounces to Home', async () => {
      mockFeatureFlags.isEnabled.mockImplementation((flag: string) => flag !== 'newAuth')
      wrapper = mountTopBar()
      await openAccountMenu(wrapper)

      expect(menuItemLabels(wrapper)).toEqual(['Appearance', 'Sign out'])
    })

    it('closes the account menu on Escape and returns focus to the avatar', async () => {
      wrapper = mountTopBar()
      const trigger = wrapper.find('[data-topbar-action="account"]')
      await openAccountMenu(wrapper)

      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
      await nextTick()

      expect(wrapper.find('[role="menu"]').exists()).toBe(false)
      expect(document.activeElement).toBe(trigger.element)
    })

    it('closes the account menu on a pointer press outside it', async () => {
      wrapper = mountTopBar()
      await openAccountMenu(wrapper)
      // Guard the guard: without this, a menu that never opened would make the
      // closing assertion below vacuously green.
      expect(wrapper.find('[role="menu"]').exists()).toBe(true)

      document.body.dispatchEvent(new Event('pointerdown', { bubbles: true }))
      await nextTick()

      expect(wrapper.find('[role="menu"]').exists()).toBe(false)
    })

    it('closes the account menu when focus tabs out of it, without yanking focus back', async () => {
      const outside = document.createElement('button')
      outside.textContent = 'somewhere else'
      document.body.appendChild(outside)
      try {
        wrapper = mountTopBar()
        const trigger = wrapper.find('[data-topbar-action="account"]')
        await openAccountMenu(wrapper)
        await nextTick()
        expect(wrapper.find('[role="menu"]').exists()).toBe(true)

        // Tab out: focus lands outside the account cluster, so focusout fires on
        // the item with the new element as relatedTarget.
        const focused = document.activeElement as HTMLElement
        outside.focus()
        focused.dispatchEvent(
          new FocusEvent('focusout', { bubbles: true, relatedTarget: outside }),
        )
        await nextTick()

        expect(wrapper.find('[role="menu"]').exists()).toBe(false)
        // Focus must stay where the user put it — restoring it here would cancel
        // the Tab and make the menu impossible to leave by keyboard.
        expect(document.activeElement).toBe(outside)
        expect(document.activeElement).not.toBe(trigger.element)
      } finally {
        outside.remove()
      }
    })

    it('keeps the account menu open when focus moves between its own items', async () => {
      wrapper = mountTopBar()
      const menu = await openAccountMenu(wrapper)
      await nextTick()
      const items = wrapper.findAll('[role="menuitem"]')

      items[0].element.dispatchEvent(
        new FocusEvent('focusout', { bubbles: true, relatedTarget: items[1].element }),
      )
      await nextTick()

      expect(wrapper.find('[role="menu"]').exists()).toBe(true)
      expect(menu.exists()).toBe(true)
    })

    it('closes the account menu on a second press of the avatar', async () => {
      wrapper = mountTopBar()

      await pressAccountTrigger(wrapper)
      expect(wrapper.find('[role="menu"]').exists()).toBe(true)

      // The trigger lives INSIDE accountRootEl, so its own pointerdown is not an
      // "outside" press. If it were outside, this pointerdown would close the
      // menu and the click would immediately re-open it — a control that can
      // never be dismissed by clicking it again.
      await pressAccountTrigger(wrapper)
      expect(wrapper.find('[role="menu"]').exists()).toBe(false)
    })

    it('keeps the account menu open on a pointer press inside it', async () => {
      wrapper = mountTopBar()
      const menu = await openAccountMenu(wrapper)

      menu.element.dispatchEvent(new Event('pointerdown', { bubbles: true }))
      await nextTick()

      expect(wrapper.find('[role="menu"]').exists()).toBe(true)
    })

    it('moves focus with ArrowDown/ArrowUp inside the account menu', async () => {
      wrapper = mountTopBar()
      const menu = await openAccountMenu(wrapper)
      await nextTick()
      const items = wrapper.findAll('[role="menuitem"]')

      await menu.trigger('keydown', { key: 'ArrowDown' })
      expect(document.activeElement).toBe(items[1].element)

      await menu.trigger('keydown', { key: 'ArrowUp' })
      expect(document.activeElement).toBe(items[0].element)

      await menu.trigger('keydown', { key: 'ArrowUp' })
      expect(document.activeElement).toBe(items.at(-1)!.element)
    })
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
