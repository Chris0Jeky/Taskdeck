import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { agentApi } from '../../api/agentApi'
import { useAgentStore } from '../../store/agentStore'
import type { AgentProfile, AgentRun, AgentRunDetail } from '../../types/agent'

const toastMocks = vi.hoisted(() => ({
  error: vi.fn(),
  success: vi.fn(),
  info: vi.fn(),
  warning: vi.fn(),
}))

vi.mock('../../utils/demoMode', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../utils/demoMode')>()
  return {
    ...actual,
    isDemoMode: false,
  }
})

vi.mock('../../api/agentApi', () => ({
  agentApi: {
    listProfiles: vi.fn(),
    getProfile: vi.fn(),
    listRuns: vi.fn(),
    getRunDetail: vi.fn(),
  },
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => toastMocks,
}))

const MOCK_PROFILE: AgentProfile = {
  id: 'profile-1',
  userId: 'user-1',
  name: 'Test Agent',
  description: 'A test agent',
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

const MOCK_RUN_DETAIL: AgentRunDetail = {
  ...MOCK_RUN,
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
  ],
}

describe('agentStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('starts with empty default state', () => {
    const store = useAgentStore()
    expect(store.profiles).toEqual([])
    expect(store.profilesLoading).toBe(false)
    expect(store.profilesError).toBeNull()
    expect(store.runs).toEqual([])
    expect(store.runsLoading).toBe(false)
    expect(store.runsError).toBeNull()
    expect(store.runDetail).toBeNull()
    expect(store.runDetailLoading).toBe(false)
    expect(store.runDetailError).toBeNull()
  })

  describe('fetchProfiles', () => {
    it('populates profiles from the API', async () => {
      const store = useAgentStore()
      vi.mocked(agentApi.listProfiles).mockResolvedValue([MOCK_PROFILE])

      await store.fetchProfiles()

      expect(agentApi.listProfiles).toHaveBeenCalled()
      expect(store.profiles).toEqual([MOCK_PROFILE])
      expect(store.profilesError).toBeNull()
      expect(store.profilesLoading).toBe(false)
    })

    it('sets loading true during fetch and false after', async () => {
      const store = useAgentStore()
      let resolveRequest: ((value: AgentProfile[]) => void) | null = null

      vi.mocked(agentApi.listProfiles).mockImplementation(
        () => new Promise((resolve) => { resolveRequest = resolve }),
      )

      const fetchPromise = store.fetchProfiles()
      expect(store.profilesLoading).toBe(true)

      resolveRequest?.([])
      await fetchPromise
      expect(store.profilesLoading).toBe(false)
    })

    it('records error and shows toast on failure', async () => {
      const store = useAgentStore()
      vi.mocked(agentApi.listProfiles).mockRejectedValue(new Error('Network error'))

      await expect(store.fetchProfiles()).rejects.toBeInstanceOf(Error)

      expect(store.profilesError).toBe('Network error')
      expect(toastMocks.error).toHaveBeenCalledWith('Network error')
      expect(store.profilesLoading).toBe(false)
    })
  })

  describe('fetchRuns', () => {
    it('populates runs from the API', async () => {
      const store = useAgentStore()
      vi.mocked(agentApi.listRuns).mockResolvedValue([MOCK_RUN])

      await store.fetchRuns('profile-1')

      expect(agentApi.listRuns).toHaveBeenCalledWith('profile-1', 100)
      expect(store.runs).toEqual([MOCK_RUN])
      expect(store.runsError).toBeNull()
    })

    it('records error and shows toast on failure', async () => {
      const store = useAgentStore()
      vi.mocked(agentApi.listRuns).mockRejectedValue(new Error('Forbidden'))

      await expect(store.fetchRuns('profile-1')).rejects.toBeInstanceOf(Error)

      expect(store.runsError).toBe('Forbidden')
      expect(toastMocks.error).toHaveBeenCalledWith('Forbidden')
    })

    it('sets loading true during fetch and false after', async () => {
      const store = useAgentStore()
      let resolveRequest: ((value: AgentRun[]) => void) | null = null

      vi.mocked(agentApi.listRuns).mockImplementation(
        () => new Promise((resolve) => { resolveRequest = resolve }),
      )

      const fetchPromise = store.fetchRuns('profile-1')
      expect(store.runsLoading).toBe(true)

      resolveRequest?.([])
      await fetchPromise
      expect(store.runsLoading).toBe(false)
    })
  })

  describe('fetchRunDetail', () => {
    it('populates runDetail from the API', async () => {
      const store = useAgentStore()
      vi.mocked(agentApi.getRunDetail).mockResolvedValue(MOCK_RUN_DETAIL)

      await store.fetchRunDetail('profile-1', 'run-1')

      expect(agentApi.getRunDetail).toHaveBeenCalledWith('profile-1', 'run-1')
      expect(store.runDetail).toEqual(MOCK_RUN_DETAIL)
      expect(store.runDetailError).toBeNull()
    })

    it('records error and shows toast on failure', async () => {
      const store = useAgentStore()
      vi.mocked(agentApi.getRunDetail).mockRejectedValue(new Error('Not found'))

      await expect(store.fetchRunDetail('profile-1', 'run-1')).rejects.toBeInstanceOf(Error)

      expect(store.runDetailError).toBe('Not found')
      expect(toastMocks.error).toHaveBeenCalledWith('Not found')
    })

    it('sets loading true during fetch and false after', async () => {
      const store = useAgentStore()
      let resolveRequest: ((value: AgentRunDetail) => void) | null = null

      vi.mocked(agentApi.getRunDetail).mockImplementation(
        () => new Promise((resolve) => { resolveRequest = resolve }),
      )

      const fetchPromise = store.fetchRunDetail('profile-1', 'run-1')
      expect(store.runDetailLoading).toBe(true)

      resolveRequest?.(MOCK_RUN_DETAIL)
      await fetchPromise
      expect(store.runDetailLoading).toBe(false)
    })
  })

  describe('clearRuns', () => {
    it('resets runs and error', async () => {
      const store = useAgentStore()
      vi.mocked(agentApi.listRuns).mockResolvedValue([MOCK_RUN])
      await store.fetchRuns('profile-1')
      expect(store.runs).toHaveLength(1)

      store.clearRuns()

      expect(store.runs).toEqual([])
      expect(store.runsError).toBeNull()
    })
  })

  describe('clearRunDetail', () => {
    it('resets runDetail and error', async () => {
      const store = useAgentStore()
      vi.mocked(agentApi.getRunDetail).mockResolvedValue(MOCK_RUN_DETAIL)
      await store.fetchRunDetail('profile-1', 'run-1')
      expect(store.runDetail).not.toBeNull()

      store.clearRunDetail()

      expect(store.runDetail).toBeNull()
      expect(store.runDetailError).toBeNull()
    })
  })
})

describe('agentStore (demo mode)', () => {
  beforeEach(async () => {
    vi.resetModules()
    vi.doMock('../../utils/demoMode', () => ({ isDemoMode: true }))
    vi.doMock('../../api/agentApi', () => ({
      agentApi: {
        listProfiles: vi.fn(),
        getProfile: vi.fn(),
        listRuns: vi.fn(),
        getRunDetail: vi.fn(),
      },
    }))
    vi.doMock('../../store/toastStore', () => ({
      useToastStore: () => ({ error: vi.fn(), success: vi.fn(), info: vi.fn(), warning: vi.fn() }),
    }))
    setActivePinia(createPinia())
  })

  it('fetchProfiles returns empty without calling API in demo mode', async () => {
    const { useAgentStore: useDemoStore } = await import('../../store/agentStore')
    const { agentApi: demoApi } = await import('../../api/agentApi')
    const store = useDemoStore()

    await store.fetchProfiles()

    expect(demoApi.listProfiles).not.toHaveBeenCalled()
    expect(store.profiles).toEqual([])
    expect(store.profilesLoading).toBe(false)
  })

  it('fetchRuns returns empty without calling API in demo mode', async () => {
    const { useAgentStore: useDemoStore } = await import('../../store/agentStore')
    const { agentApi: demoApi } = await import('../../api/agentApi')
    const store = useDemoStore()

    await store.fetchRuns('profile-1')

    expect(demoApi.listRuns).not.toHaveBeenCalled()
    expect(store.runs).toEqual([])
    expect(store.runsLoading).toBe(false)
  })

  it('fetchRunDetail returns null without calling API in demo mode', async () => {
    const { useAgentStore: useDemoStore } = await import('../../store/agentStore')
    const { agentApi: demoApi } = await import('../../api/agentApi')
    const store = useDemoStore()

    await store.fetchRunDetail('profile-1', 'run-1')

    expect(demoApi.getRunDetail).not.toHaveBeenCalled()
    expect(store.runDetail).toBeNull()
    expect(store.runDetailLoading).toBe(false)
  })
})
