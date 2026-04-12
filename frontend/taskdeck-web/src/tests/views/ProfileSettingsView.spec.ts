import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import ProfileSettingsView from '../../views/ProfileSettingsView.vue'

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
