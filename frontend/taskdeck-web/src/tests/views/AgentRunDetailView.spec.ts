import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { reactive } from 'vue'
import AgentRunDetailView from '../../views/AgentRunDetailView.vue'
import type { AgentProfile, AgentRunDetail } from '../../types/agent'

const mockPush = vi.fn()

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: mockPush }),
  useRoute: () => ({ params: { agentId: 'profile-1', runId: 'run-1' } }),
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

const MOCK_RUN_DETAIL: AgentRunDetail = {
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
  events: [
    {
      id: 'event-1',
      runId: 'run-1',
      sequenceNumber: 0,
      eventType: 'run.started',
      payload: '{}',
      timestamp: '2026-04-01T10:00:00Z',
    },
    {
      id: 'event-2',
      runId: 'run-1',
      sequenceNumber: 1,
      eventType: 'context.gathered',
      payload: '{"captureCount": 5}',
      timestamp: '2026-04-01T10:00:10Z',
    },
    {
      id: 'event-3',
      runId: 'run-1',
      sequenceNumber: 2,
      eventType: 'run.completed',
      payload: '{}',
      timestamp: '2026-04-01T10:01:00Z',
    },
  ],
}

const mockAgentStore = reactive({
  profiles: [MOCK_PROFILE] as AgentProfile[],
  profilesLoading: false,
  profilesError: null as string | null,
  runDetail: null as AgentRunDetail | null,
  runDetailLoading: false,
  runDetailError: null as string | null,
  fetchProfiles: vi.fn().mockResolvedValue(undefined),
  fetchRunDetail: vi.fn().mockResolvedValue(undefined),
  clearRunDetail: vi.fn(),
})

vi.mock('../../store/agentStore', () => ({
  useAgentStore: () => mockAgentStore,
}))

async function waitForUi() {
  await flushPromises()
  await flushPromises()
}

describe('AgentRunDetailView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockAgentStore.profiles = [MOCK_PROFILE]
    mockAgentStore.runDetail = null
    mockAgentStore.runDetailLoading = false
    mockAgentStore.runDetailError = null
  })

  it('calls fetchRunDetail on mount', async () => {
    mount(AgentRunDetailView)
    await waitForUi()

    expect(mockAgentStore.fetchRunDetail).toHaveBeenCalledWith('profile-1', 'run-1')
  })

  it('shows loading state', async () => {
    mockAgentStore.runDetailLoading = true
    const wrapper = mount(AgentRunDetailView)
    await waitForUi()

    expect(wrapper.text()).toContain('Loading run detail...')
  })

  it('shows error state with retry button', async () => {
    mockAgentStore.runDetailError = 'Not found'
    const wrapper = mount(AgentRunDetailView)
    await waitForUi()

    expect(wrapper.text()).toContain('Not found')
    expect(wrapper.find('.td-run-detail__state--error button').exists()).toBe(true)
  })

  it('renders run header with objective, status, and metadata', async () => {
    mockAgentStore.runDetail = MOCK_RUN_DETAIL
    const wrapper = mount(AgentRunDetailView)
    await waitForUi()

    expect(wrapper.text()).toContain('Triage inbox captures')
    expect(wrapper.text()).toContain('Completed')
    expect(wrapper.text()).toContain('Steps: 3')
    expect(wrapper.text()).toContain('Tokens: 1,200')
    expect(wrapper.text()).toContain('Triaged 5 captures')
  })

  it('renders timeline events in sequence order', async () => {
    mockAgentStore.runDetail = MOCK_RUN_DETAIL
    const wrapper = mount(AgentRunDetailView)
    await waitForUi()

    const items = wrapper.findAll('.td-timeline__item')
    expect(items).toHaveLength(3)
    expect(items[0].text()).toContain('Run started')
    expect(items[0].text()).toContain('Step 1')
    expect(items[1].text()).toContain('Context gathered')
    expect(items[1].text()).toContain('Step 2')
    expect(items[2].text()).toContain('Run completed')
    expect(items[2].text()).toContain('Step 3')
  })

  it('renders event payload when non-empty', async () => {
    mockAgentStore.runDetail = MOCK_RUN_DETAIL
    const wrapper = mount(AgentRunDetailView)
    await waitForUi()

    const payloads = wrapper.findAll('.td-timeline__payload')
    // Only event-2 has non-empty payload (captureCount: 5)
    expect(payloads).toHaveLength(1)
    expect(payloads[0].text()).toContain('captureCount')
  })

  it('shows proposal link when proposalId is present', async () => {
    mockAgentStore.runDetail = MOCK_RUN_DETAIL
    const wrapper = mount(AgentRunDetailView)
    await waitForUi()

    const proposalLink = wrapper.find('.td-run-detail__proposal-link')
    expect(proposalLink.exists()).toBe(true)
    expect(proposalLink.text()).toBe('View linked proposal')
  })

  it('navigates to proposal review when proposal link is clicked', async () => {
    mockAgentStore.runDetail = MOCK_RUN_DETAIL
    const wrapper = mount(AgentRunDetailView)
    await waitForUi()

    await wrapper.find('.td-run-detail__proposal-link').trigger('click')
    expect(mockPush).toHaveBeenCalledWith('/workspace/review?proposalId=proposal-1')
  })

  it('does not show proposal link when proposalId is null', async () => {
    mockAgentStore.runDetail = { ...MOCK_RUN_DETAIL, proposalId: null }
    const wrapper = mount(AgentRunDetailView)
    await waitForUi()

    expect(wrapper.find('.td-run-detail__proposal-link').exists()).toBe(false)
  })

  it('shows failure reason for failed runs', async () => {
    mockAgentStore.runDetail = {
      ...MOCK_RUN_DETAIL,
      status: 'Failed',
      failureReason: 'Provider timeout',
    }
    const wrapper = mount(AgentRunDetailView)
    await waitForUi()

    expect(wrapper.text()).toContain('Provider timeout')
  })

  it('does not show live indicator for terminal status', async () => {
    mockAgentStore.runDetail = MOCK_RUN_DETAIL // status: Completed
    const wrapper = mount(AgentRunDetailView)
    await waitForUi()

    expect(wrapper.find('.td-run-detail__live-indicator').exists()).toBe(false)
  })

  it('shows live indicator for non-terminal status', async () => {
    mockAgentStore.runDetail = { ...MOCK_RUN_DETAIL, status: 'Planning' }
    const wrapper = mount(AgentRunDetailView)
    await waitForUi()

    const indicator = wrapper.find('.td-run-detail__live-indicator')
    expect(indicator.exists()).toBe(true)
    expect(indicator.text()).toContain('Run is in progress')
  })

  it('navigates back to runs list when back button is clicked', async () => {
    const wrapper = mount(AgentRunDetailView)
    await waitForUi()

    await wrapper.find('.td-run-detail__back').trigger('click')
    expect(mockPush).toHaveBeenCalledWith('/workspace/agents/profile-1/runs')
  })

  it('calls clearRunDetail on unmount', async () => {
    const wrapper = mount(AgentRunDetailView)
    await waitForUi()

    wrapper.unmount()
    expect(mockAgentStore.clearRunDetail).toHaveBeenCalled()
  })

  it('shows empty timeline when no events exist', async () => {
    mockAgentStore.runDetail = { ...MOCK_RUN_DETAIL, events: [] }
    const wrapper = mount(AgentRunDetailView)
    await waitForUi()

    expect(wrapper.text()).toContain('No events recorded')
  })
})
