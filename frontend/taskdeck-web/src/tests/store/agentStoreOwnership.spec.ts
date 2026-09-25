import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { agentApi } from '../../api/agentApi'
import { useAgentStore } from '../../store/agentStore'
import { useSessionStore } from '../../store/sessionStore'
import type { AgentProfile, AgentRun, AgentRunDetail } from '../../types/agent'

const toastMocks = vi.hoisted(() => ({
  error: vi.fn(),
  success: vi.fn(),
  info: vi.fn(),
  warning: vi.fn(),
}))

vi.mock('../../utils/demoMode', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../utils/demoMode')>()
  return { ...actual, isDemoMode: false }
})

vi.mock('../../api/agentApi', () => ({
  agentApi: {
    listProfiles: vi.fn(),
    getProfile: vi.fn(),
    listRuns: vi.fn(),
    getRunDetail: vi.fn(),
  },
}))

vi.mock('../../api/authApi', () => ({
  authApi: {
    login: vi.fn(),
    register: vi.fn(),
    changePassword: vi.fn(),
    refreshToken: vi.fn(),
    exchangeOAuthCode: vi.fn(),
    exchangeOidcCode: vi.fn(),
  },
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => toastMocks,
}))

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((yes, no) => {
    resolve = yes
    reject = no
  })
  return { promise, resolve, reject }
}

function sessionToken(id: string): string {
  const payload = btoa(JSON.stringify({ exp: 4_102_444_800, jti: id }))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/g, '')
  return `header.${payload}.sig`
}

function profile(id: string): AgentProfile {
  return {
    id,
    userId: 'user-a',
    name: `Agent ${id}`,
    description: '',
    templateKey: 'triage-assistant',
    scopeType: 'Workspace',
    scopeBoardId: null,
    policyJson: '{}',
    isEnabled: true,
    createdAt: '2026-09-21T00:00:00Z',
    updatedAt: '2026-09-21T00:00:00Z',
  }
}

function run(agentProfileId: string, id: string): AgentRun {
  return {
    id,
    agentProfileId,
    userId: 'user-a',
    boardId: null,
    triggerType: 'manual',
    objective: id,
    status: 'Completed',
    summary: null,
    failureReason: null,
    proposalId: null,
    stepsExecuted: 1,
    tokensUsed: 1,
    approxCostUsd: null,
    startedAt: '2026-09-21T00:00:00Z',
    completedAt: '2026-09-21T00:00:01Z',
    createdAt: '2026-09-21T00:00:00Z',
    updatedAt: '2026-09-21T00:00:01Z',
  }
}

function detail(agentProfileId: string, id: string): AgentRunDetail {
  return { ...run(agentProfileId, id), events: [] }
}

describe('agentStore async ownership', () => {
  let session: ReturnType<typeof useSessionStore>
  let store: ReturnType<typeof useAgentStore>

  beforeEach(() => {
    setActivePinia(createPinia())
    session = useSessionStore()
    session.userId = 'user-a'
    store = useAgentStore()
    vi.clearAllMocks()
  })

  it('keeps the newest profile read when responses settle in reverse order', async () => {
    const older = deferred<AgentProfile[]>()
    const newer = deferred<AgentProfile[]>()
    vi.mocked(agentApi.listProfiles)
      .mockReturnValueOnce(older.promise)
      .mockReturnValueOnce(newer.promise)

    const oldRequest = store.fetchProfiles()
    const newRequest = store.fetchProfiles()
    newer.resolve([profile('new')])
    await newRequest
    older.resolve([profile('old')])
    await oldRequest

    expect(store.profiles.map(item => item.id)).toEqual(['new'])
  })

  it('keeps the newest A run list across A-old to B to A-new navigation', async () => {
    const oldA = deferred<AgentRun[]>()
    const boardB = deferred<AgentRun[]>()
    const newA = deferred<AgentRun[]>()
    vi.mocked(agentApi.listRuns)
      .mockReturnValueOnce(oldA.promise)
      .mockReturnValueOnce(boardB.promise)
      .mockReturnValueOnce(newA.promise)

    const oldRequest = store.fetchRuns('agent-a')
    store.clearRuns()
    const bRequest = store.fetchRuns('agent-b')
    store.clearRuns()
    const newRequest = store.fetchRuns('agent-a')

    newA.resolve([run('agent-a', 'new-a')])
    await newRequest
    boardB.resolve([run('agent-b', 'b')])
    await bRequest
    oldA.resolve([run('agent-a', 'old-a')])
    await oldRequest

    expect(store.runs.map(item => item.id)).toEqual(['new-a'])
  })

  it('keeps the newest run detail across repeated route identities', async () => {
    const oldA = deferred<AgentRunDetail>()
    const other = deferred<AgentRunDetail>()
    const newA = deferred<AgentRunDetail>()
    vi.mocked(agentApi.getRunDetail)
      .mockReturnValueOnce(oldA.promise)
      .mockReturnValueOnce(other.promise)
      .mockReturnValueOnce(newA.promise)

    const oldRequest = store.fetchRunDetail('agent-a', 'run-a')
    store.clearRunDetail()
    const otherRequest = store.fetchRunDetail('agent-b', 'run-b')
    store.clearRunDetail()
    const newRequest = store.fetchRunDetail('agent-a', 'run-a')

    newA.resolve(detail('agent-a', 'new-a'))
    await newRequest
    other.resolve(detail('agent-b', 'other'))
    await otherRequest
    oldA.resolve(detail('agent-a', 'old-a'))
    await oldRequest

    expect(store.runDetail?.id).toBe('new-a')
  })

  it('clearRuns invalidates a pending success', async () => {
    const pending = deferred<AgentRun[]>()
    vi.mocked(agentApi.listRuns).mockReturnValue(pending.promise)

    const request = store.fetchRuns('agent-a')
    expect(store.runsLoading).toBe(true)
    store.clearRuns()
    expect(store.runsLoading).toBe(false)

    pending.resolve([run('agent-a', 'late')])
    await request

    expect(store.runs).toEqual([])
    expect(store.runsError).toBeNull()
  })

  it('clearRunDetail invalidates a pending failure without stale UI', async () => {
    const pending = deferred<AgentRunDetail>()
    vi.mocked(agentApi.getRunDetail).mockReturnValue(pending.promise)

    const request = store.fetchRunDetail('agent-a', 'run-a')
    store.clearRunDetail()
    pending.reject(new Error('stale detail failure'))
    await expect(request).rejects.toThrow('stale detail failure')

    expect(store.runDetail).toBeNull()
    expect(store.runDetailError).toBeNull()
    expect(store.runDetailLoading).toBe(false)
    expect(toastMocks.error).not.toHaveBeenCalled()
  })

  it('does not let an older finally clear a newer same-lane loading owner', async () => {
    const older = deferred<AgentRun[]>()
    const newer = deferred<AgentRun[]>()
    vi.mocked(agentApi.listRuns)
      .mockReturnValueOnce(older.promise)
      .mockReturnValueOnce(newer.promise)

    const oldRequest = store.fetchRuns('agent-a')
    const newRequest = store.fetchRuns('agent-a')
    older.resolve([run('agent-a', 'old')])
    await oldRequest

    expect(store.runsLoading).toBe(true)

    newer.resolve([run('agent-a', 'new')])
    await newRequest
    expect(store.runsLoading).toBe(false)
  })

  it('preserves loaded route data while invalidating old-token reads on refresh', async () => {
    setActivePinia(createPinia())
    session = useSessionStore()
    session.userId = 'user-a'
    session.token = sessionToken('old')
    store = useAgentStore()

    store.profiles = [profile('existing')]
    store.runs = [run('agent-a', 'existing')]
    store.runDetail = detail('agent-a', 'existing')

    const profiles = deferred<AgentProfile[]>()
    const runs = deferred<AgentRun[]>()
    const runDetail = deferred<AgentRunDetail>()
    vi.mocked(agentApi.listProfiles).mockReturnValue(profiles.promise)
    vi.mocked(agentApi.listRuns).mockReturnValue(runs.promise)
    vi.mocked(agentApi.getRunDetail).mockReturnValue(runDetail.promise)

    const profileRequest = store.fetchProfiles()
    const runsRequest = store.fetchRuns('agent-a')
    const detailRequest = store.fetchRunDetail('agent-a', 'run-a')

    session.token = sessionToken('new')

    expect(store.profiles.map(item => item.id)).toEqual(['existing'])
    expect(store.runs.map(item => item.id)).toEqual(['existing'])
    expect(store.runDetail?.id).toBe('existing')
    expect(store.profilesLoading).toBe(false)
    expect(store.runsLoading).toBe(false)
    expect(store.runDetailLoading).toBe(false)

    profiles.resolve([profile('old-token')])
    runs.reject(new Error('old-token failure'))
    runDetail.resolve(detail('agent-a', 'old-token'))

    await profileRequest
    await expect(runsRequest).rejects.toThrow('old-token failure')
    await detailRequest

    expect(store.profiles.map(item => item.id)).toEqual(['existing'])
    expect(store.runs.map(item => item.id)).toEqual(['existing'])
    expect(store.runDetail?.id).toBe('existing')
    expect(store.runsError).toBeNull()
    expect(toastMocks.error).not.toHaveBeenCalled()
  })

  it('retries empty initial route reads after same-user token rotation', async () => {
    setActivePinia(createPinia())
    session = useSessionStore()
    session.userId = 'user-a'
    session.token = sessionToken('old')
    store = useAgentStore()

    const oldProfiles = deferred<AgentProfile[]>()
    const freshProfiles = deferred<AgentProfile[]>()
    const oldRuns = deferred<AgentRun[]>()
    const freshRuns = deferred<AgentRun[]>()
    const oldDetail = deferred<AgentRunDetail>()
    const freshDetail = deferred<AgentRunDetail>()
    vi.mocked(agentApi.listProfiles)
      .mockReturnValueOnce(oldProfiles.promise)
      .mockReturnValueOnce(freshProfiles.promise)
    vi.mocked(agentApi.listRuns)
      .mockReturnValueOnce(oldRuns.promise)
      .mockReturnValueOnce(freshRuns.promise)
    vi.mocked(agentApi.getRunDetail)
      .mockReturnValueOnce(oldDetail.promise)
      .mockReturnValueOnce(freshDetail.promise)

    const profileRequest = store.fetchProfiles()
    const runsRequest = store.fetchRuns('agent-a')
    const detailRequest = store.fetchRunDetail('agent-a', 'run-a')

    session.token = sessionToken('new')

    expect(agentApi.listProfiles).toHaveBeenCalledTimes(2)
    expect(agentApi.listRuns).toHaveBeenCalledTimes(2)
    expect(agentApi.getRunDetail).toHaveBeenCalledTimes(2)
    expect(store.profilesLoading).toBe(true)
    expect(store.runsLoading).toBe(true)
    expect(store.runDetailLoading).toBe(true)

    oldProfiles.resolve([profile('old-token')])
    oldRuns.reject(new Error('old-token failure'))
    oldDetail.resolve(detail('agent-a', 'old-token'))
    await profileRequest
    await expect(runsRequest).rejects.toThrow('old-token failure')
    await detailRequest

    expect(store.profiles).toEqual([])
    expect(store.runs).toEqual([])
    expect(store.runDetail).toBeNull()
    expect(store.runsError).toBeNull()
    expect(toastMocks.error).not.toHaveBeenCalled()

    freshProfiles.resolve([profile('fresh-token')])
    freshRuns.resolve([run('agent-a', 'fresh-token')])
    freshDetail.resolve(detail('agent-a', 'fresh-token'))

    await vi.waitFor(() => {
      expect(store.profiles.map(item => item.id)).toEqual(['fresh-token'])
      expect(store.runs.map(item => item.id)).toEqual(['fresh-token'])
      expect(store.runDetail?.id).toBe('fresh-token')
      expect(store.profilesLoading).toBe(false)
      expect(store.runsLoading).toBe(false)
      expect(store.runDetailLoading).toBe(false)
    })
  })

  it('clears every surface and invalidates pending reads on session replacement', async () => {
    store.profiles = [profile('existing')]
    store.runs = [run('agent-a', 'existing')]
    store.runDetail = detail('agent-a', 'existing')

    const profiles = deferred<AgentProfile[]>()
    const runs = deferred<AgentRun[]>()
    const runDetail = deferred<AgentRunDetail>()
    vi.mocked(agentApi.listProfiles).mockReturnValue(profiles.promise)
    vi.mocked(agentApi.listRuns).mockReturnValue(runs.promise)
    vi.mocked(agentApi.getRunDetail).mockReturnValue(runDetail.promise)

    const profileRequest = store.fetchProfiles()
    const runsRequest = store.fetchRuns('agent-a')
    const detailRequest = store.fetchRunDetail('agent-a', 'run-a')

    session.userId = null
    session.userId = 'user-a'

    expect(store.profiles).toEqual([])
    expect(store.runs).toEqual([])
    expect(store.runDetail).toBeNull()
    expect(store.profilesLoading).toBe(false)
    expect(store.runsLoading).toBe(false)
    expect(store.runDetailLoading).toBe(false)

    profiles.resolve([profile('old-session')])
    runs.resolve([run('agent-a', 'old-session')])
    runDetail.resolve(detail('agent-a', 'old-session'))
    await Promise.all([profileRequest, runsRequest, detailRequest])

    expect(store.profiles).toEqual([])
    expect(store.runs).toEqual([])
    expect(store.runDetail).toBeNull()
  })

  it('suppresses a stale failure after logout and same-user login', async () => {
    const pending = deferred<AgentRun[]>()
    vi.mocked(agentApi.listRuns).mockReturnValue(pending.promise)
    const request = store.fetchRuns('agent-a')

    session.userId = null
    session.userId = 'user-a'
    pending.reject(new Error('old-session failure'))
    await expect(request).rejects.toThrow('old-session failure')

    expect(store.runsError).toBeNull()
    expect(store.runsLoading).toBe(false)
    expect(toastMocks.error).not.toHaveBeenCalled()
  })

  it('keeps independent lanes concurrent with truthful loading', async () => {
    const profiles = deferred<AgentProfile[]>()
    const runs = deferred<AgentRun[]>()
    const runDetail = deferred<AgentRunDetail>()
    vi.mocked(agentApi.listProfiles).mockReturnValue(profiles.promise)
    vi.mocked(agentApi.listRuns).mockReturnValue(runs.promise)
    vi.mocked(agentApi.getRunDetail).mockReturnValue(runDetail.promise)

    const profileRequest = store.fetchProfiles()
    const runsRequest = store.fetchRuns('agent-a')
    const detailRequest = store.fetchRunDetail('agent-a', 'run-a')

    profiles.resolve([profile('profile')])
    await profileRequest
    expect(store.profilesLoading).toBe(false)
    expect(store.runsLoading).toBe(true)
    expect(store.runDetailLoading).toBe(true)

    runs.resolve([run('agent-a', 'run')])
    await runsRequest
    expect(store.runsLoading).toBe(false)
    expect(store.runDetailLoading).toBe(true)

    runDetail.resolve(detail('agent-a', 'run'))
    await detailRequest
    expect(store.runDetailLoading).toBe(false)
  })
})
