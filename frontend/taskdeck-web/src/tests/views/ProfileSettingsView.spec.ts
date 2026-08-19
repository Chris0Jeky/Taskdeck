import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import ProfileSettingsView from '../../views/ProfileSettingsView.vue'
import profileSource from '../../views/ProfileSettingsView.vue?raw'

const sessionMocks = vi.hoisted(() => ({
  state: {
    username: 'test-user',
    email: 'test@example.com',
    userId: 'user-1',
    defaultRole: null as number | null,
    error: null as string | null,
  },
  changePassword: vi.fn(),
  requireUserId: vi.fn(() => 'user-1'),
}))

const featureFlagMocks = vi.hoisted(() => ({
  isEnabled: vi.fn(() => false),
  setFlag: vi.fn(),
  resetAll: vi.fn(),
}))

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: () => ({
    username: sessionMocks.state.username,
    email: sessionMocks.state.email,
    userId: sessionMocks.state.userId,
    defaultRole: sessionMocks.state.defaultRole,
    error: sessionMocks.state.error,
    changePassword: sessionMocks.changePassword,
    requireUserId: sessionMocks.requireUserId,
  }),
}))

vi.mock('../../store/featureFlagStore', () => ({
  useFeatureFlagStore: () => ({
    isEnabled: featureFlagMocks.isEnabled,
    setFlag: featureFlagMocks.setFlag,
    resetAll: featureFlagMocks.resetAll,
  }),
}))

vi.mock('../../api/authApi', () => ({
  authApi: {
    getProviders: vi.fn().mockResolvedValue({ gitHub: false }),
    getLinkedAccounts: vi.fn().mockResolvedValue([]),
    linkGitHub: vi.fn(),
    unlinkGitHub: vi.fn(),
  },
}))

vi.mock('../../utils/demoMode', () => ({
  isDemoMode: false,
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({
    replace: vi.fn(),
    push: vi.fn(),
  }),
  useRoute: () => ({
    query: {},
    path: '/workspace/settings',
  }),
}))

describe('ProfileSettingsView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    sessionMocks.state.defaultRole = null
  })

  it('renders role and ops access sections with Unknown defaults', () => {
    const wrapper = mount(ProfileSettingsView)

    expect(wrapper.text()).toContain('Role')
    expect(wrapper.text()).toContain('Unknown')
    expect(wrapper.text()).toContain('Ops Access')
    expect(wrapper.text()).toContain('Ops CLI template access is limited; request elevated access for admin templates.')
  })

  it('shows full ops capability summary for admin role', () => {
    sessionMocks.state.defaultRole = 1
    const wrapper = mount(ProfileSettingsView)

    expect(wrapper.text()).toContain('Admin')
    expect(wrapper.text()).toContain('Can run all default Ops CLI templates.')
  })

  it('shows editor-specific ops capability summary for editor role', () => {
    sessionMocks.state.defaultRole = 2
    const wrapper = mount(ProfileSettingsView)

    expect(wrapper.text()).toContain('Editor')
    expect(wrapper.text()).toContain('Can run editor-safe Ops templates; admin templates are restricted.')
  })
})

// ── #1808 review (MEDIUM): Legacy ("off") mode substrate guard ──
// Paper tokens exist only under `.paper` / `.paper-night` (paper-tokens.css), so
// in Legacy mode this view's `color: var(--ink, …)` resolves to the near-black
// literal while AppShell's `.td-content` still paints `--td-surface-base`
// (#131313) — ~1.05:1 on the hero. A root that sets the Paper ink MUST therefore
// also paint the Paper substrate; that is a no-op under `.paper`/`.paper-night`.
// Source is read through Vite's `?raw` rather than `node:fs` because
// `tsconfig.vitest.json` deliberately omits the "node" types.
// #1815 tracks unifying these per-view assertions into one wave-wide spec.
describe('ProfileSettingsView Legacy-mode substrate', () => {
  it('paints --paper on the root wherever it sets --ink', () => {
    const rule = profileSource.match(/^\.paper-profile \{([\s\S]*?)\}/m)?.[1]
    expect(rule, '.paper-profile root rule').toBeTruthy()
    // Guard the guard: if the ink declaration were dropped or renamed, the
    // substrate assertion below would otherwise pass vacuously.
    expect(rule).toMatch(/color:\s*var\(--ink,\s*#[0-9a-fA-F]{3,8}\s*\)/)
    expect(rule).toMatch(/background:\s*var\(--paper,\s*#[0-9a-fA-F]{3,8}\s*\)/)
  })

  // #1808 review (LOW): the class is passed to <PaperHLBtn> but had no rule
  // anywhere in the repo, silently dropping the pre-#1779 `width: 100%`.
  it('backs the paper-profile__github-btn class hook with a real rule', () => {
    const rule = profileSource.match(/^\.paper-profile__github-btn \{([\s\S]*?)\}/m)?.[1]
    expect(rule, '.paper-profile__github-btn rule').toBeTruthy()
    expect(rule).toMatch(/width:\s*100%/)
  })
})
