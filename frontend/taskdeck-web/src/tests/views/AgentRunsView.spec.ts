import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { reactive } from 'vue'
import AgentRunsView from '../../views/AgentRunsView.vue'
import type { AgentProfile, AgentRun } from '../../types/agent'

const mockPush = vi.fn()

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: mockPush }),
  useRoute: () => ({ params: { agentId: 'profile-1' } }),
}))

const MOCK_PROFILE: AgentProfile = {
  id: 'profile-1',
  userId: 'user-1',
  name: 'Triage Bot',
  description: 'Triages captures',
  templateKey: 'triage-assistant',
  scopeType: 'Workspace',
  scopeBoardId: null,
  policyJson: '{}',
  isEnabled: true,
  createdAt: '2026-04-01T00:00:00Z',
  updatedAt: '2026-04-01T00:00:00Z',
}

const MOCK_RUN: AgentRun = {
  id: 'run-1',
  agentProfileId: 'profile-1',
  userId: 'user-1',
  boardId: null,
  triggerType: 'manual',
  objective: 'Triage inbox captures',
  status: 'Completed',
  summary: 'Triaged 5 captures',
  failureReason: null,
  proposalId: 'proposal-1',
  stepsExecuted: 3,
  tokensUsed: 1200,
  approxCostUsd: 0.0012,
  startedAt: '2026-04-01T10:00:00Z',
  completedAt: '2026-04-01T10:01:00Z',
  createdAt: '2026-04-01T10:00:00Z',
  updatedAt: '2026-04-01T10:01:00Z',
}

const mockAgentStore = reactive({
  profiles: [MOCK_PROFILE] as AgentProfile[],
  profilesLoading: false,
  profilesError: null as string | null,
  runs: [] as AgentRun[],
  runsLoading: false,
  runsError: null as string | null,
  fetchProfiles: vi.fn().mockResolvedValue(undefined),
  fetchRuns: vi.fn().mockResolvedValue(undefined),
  clearRuns: vi.fn(),
})

vi.mock('../../store/agentStore', () => ({
  useAgentStore: () => mockAgentStore,
}))

async function waitForUi() {
  await flushPromises()
  await flushPromises()
}

describe('AgentRunsView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockAgentStore.profiles = [MOCK_PROFILE]
    mockAgentStore.runs = []
    mockAgentStore.runsLoading = false
    mockAgentStore.runsError = null
  })

  it('renders header with agent name and calls fetchRuns on mount', async () => {
    const wrapper = mount(AgentRunsView)
    await waitForUi()

    expect(wrapper.text()).toContain('Triage Bot')
    expect(mockAgentStore.fetchRuns).toHaveBeenCalledWith('profile-1')
  })

  it('shows loading state', async () => {
    mockAgentStore.runsLoading = true
    const wrapper = mount(AgentRunsView)
    await waitForUi()

    expect(wrapper.text()).toContain('Loading runs...')
  })

  it('shows error state with retry button', async () => {
    mockAgentStore.runsError = 'Something failed'
    const wrapper = mount(AgentRunsView)
    await waitForUi()

    expect(wrapper.text()).toContain('Something failed')
    expect(wrapper.find('.td-agent-runs__state--error button').exists()).toBe(true)
  })

  it('shows empty state when no runs exist', async () => {
    const wrapper = mount(AgentRunsView)
    await waitForUi()

    expect(wrapper.text()).toContain('No runs yet')
  })

  it('renders run cards with objective and status', async () => {
    mockAgentStore.runs = [MOCK_RUN]
    const wrapper = mount(AgentRunsView)
    await waitForUi()

    expect(wrapper.text()).toContain('Triage inbox captures')
    expect(wrapper.text()).toContain('Completed')
    expect(wrapper.text()).toContain('Triaged 5 captures')
    expect(wrapper.text()).toContain('Proposal linked')
  })

  it('shows failure reason for failed runs', async () => {
    mockAgentStore.runs = [{
      ...MOCK_RUN,
      status: 'Failed',
      failureReason: 'Rate limit exceeded',
    }]
    const wrapper = mount(AgentRunsView)
    await waitForUi()

    expect(wrapper.text()).toContain('Rate limit exceeded')
  })

  it('navigates to run detail when card is clicked', async () => {
    mockAgentStore.runs = [MOCK_RUN]
    const wrapper = mount(AgentRunsView)
    await waitForUi()

    await wrapper.find('.td-agent-runs__card-btn').trigger('click')
    expect(mockPush).toHaveBeenCalledWith('/workspace/agents/profile-1/runs/run-1')
  })

  it('navigates back to agents list when back button is clicked', async () => {
    const wrapper = mount(AgentRunsView)
    await waitForUi()

    await wrapper.find('.td-agent-runs__back').trigger('click')
    expect(mockPush).toHaveBeenCalledWith('/workspace/agents')
  })
})
