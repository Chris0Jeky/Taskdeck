import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { reactive } from 'vue'
import AgentsView from '../../views/AgentsView.vue'
import type { AgentProfile } from '../../types/agent'

const mockPush = vi.fn()

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: mockPush }),
  useRoute: () => ({ params: {} }),
}))

const mockAgentStore = reactive({
  profiles: [] as AgentProfile[],
  profilesLoading: false,
  profilesError: null as string | null,
  fetchProfiles: vi.fn().mockResolvedValue(undefined),
})

vi.mock('../../store/agentStore', () => ({
  useAgentStore: () => mockAgentStore,
}))

const MOCK_PROFILE: AgentProfile = {
  id: 'profile-1',
  userId: 'user-1',
  name: 'Triage Bot',
  description: 'Triages incoming captures',
  templateKey: 'triage-assistant',
  scopeType: 'Workspace',
  scopeBoardId: null,
  policyJson: '{}',
  isEnabled: true,
  createdAt: '2026-04-01T00:00:00Z',
  updatedAt: '2026-04-01T00:00:00Z',
}

async function waitForUi() {
  await flushPromises()
  await flushPromises()
}

describe('AgentsView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockAgentStore.profiles = []
    mockAgentStore.profilesLoading = false
    mockAgentStore.profilesError = null
  })

  it('renders header and calls fetchProfiles on mount', async () => {
    const wrapper = mount(AgentsView)
    await waitForUi()

    expect(wrapper.text()).toContain('Agents')
    expect(mockAgentStore.fetchProfiles).toHaveBeenCalled()
  })

  it('shows loading state when profilesLoading is true', async () => {
    mockAgentStore.profilesLoading = true
    const wrapper = mount(AgentsView)
    await waitForUi()

    expect(wrapper.text()).toContain('Loading agents...')
    expect(wrapper.find('.td-agents__spinner').exists()).toBe(true)
  })

  it('shows error state with retry button', async () => {
    mockAgentStore.profilesError = 'Failed to fetch'
    const wrapper = mount(AgentsView)
    await waitForUi()

    expect(wrapper.text()).toContain('Failed to fetch')
    const retryBtn = wrapper.find('.td-agents__state--error button')
    expect(retryBtn.exists()).toBe(true)
    expect(retryBtn.text()).toBe('Retry')
  })

  it('shows empty state when no profiles exist', async () => {
    const wrapper = mount(AgentsView)
    await waitForUi()

    expect(wrapper.text()).toContain('No agents configured')
  })

  it('renders profile cards with name, status, and metadata', async () => {
    mockAgentStore.profiles = [MOCK_PROFILE]
    const wrapper = mount(AgentsView)
    await waitForUi()

    expect(wrapper.text()).toContain('Triage Bot')
    expect(wrapper.text()).toContain('Active')
    expect(wrapper.text()).toContain('Workspace')
    expect(wrapper.text()).toContain('triage-assistant')
  })

  it('shows Disabled badge for disabled profiles', async () => {
    mockAgentStore.profiles = [{ ...MOCK_PROFILE, isEnabled: false }]
    const wrapper = mount(AgentsView)
    await waitForUi()

    expect(wrapper.text()).toContain('Disabled')
    expect(wrapper.find('.td-agents__status-badge--disabled').exists()).toBe(true)
  })

  it('navigates to runs view when profile card is clicked', async () => {
    mockAgentStore.profiles = [MOCK_PROFILE]
    const wrapper = mount(AgentsView)
    await waitForUi()

    await wrapper.find('.td-agents__card-btn').trigger('click')
    expect(mockPush).toHaveBeenCalledWith('/workspace/agents/profile-1/runs')
  })

  it('profile cards have accessible labels', async () => {
    mockAgentStore.profiles = [MOCK_PROFILE]
    const wrapper = mount(AgentsView)
    await waitForUi()

    const btn = wrapper.find('.td-agents__card-btn')
    expect(btn.attributes('aria-label')).toBe('View runs for Triage Bot')
  })
})
