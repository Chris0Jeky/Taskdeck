import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import http from '../../api/http'
import { useWorkspaceStore } from '../../store/workspaceStore'
import type { WorkspaceOnboarding, WorkspaceOnboardingStep } from '../../types/workspace'

const sessionState = vi.hoisted(() => ({ isAuthenticated: true }))

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    patch: vi.fn(),
    delete: vi.fn(),
  },
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => ({ error: vi.fn(), success: vi.fn(), warning: vi.fn(), info: vi.fn() }),
}))

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: () => sessionState,
}))

vi.mock('../../utils/demoMode', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../utils/demoMode')>()
  return { ...actual, isDemoMode: false }
})

const activeStep: WorkspaceOnboardingStep = {
  stepId: 'create-first-board',
  title: 'Create a board',
  description: 'Create the first board.',
  targetSurface: 'boards',
  isComplete: false,
}

function makeGuide(overrides: Partial<WorkspaceOnboarding> = {}): WorkspaceOnboarding {
  return {
    visibility: 'active',
    isComplete: false,
    currentStepId: activeStep.stepId,
    dismissedAt: null,
    completedAt: null,
    steps: [activeStep],
    ...overrides,
  }
}

function makeDeferredPlaceholder(): WorkspaceOnboarding {
  return makeGuide({
    visibility: 'dismissed',
    currentStepId: null,
    dismissedAt: '2026-09-01T10:00:00Z',
    steps: [],
  })
}

function createDeferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((innerResolve, innerReject) => {
    resolve = innerResolve
    reject = innerReject
  })

  return { promise, resolve, reject }
}

describe('workspaceStore guide replay payload adoption (#3124)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    localStorage.clear()
    sessionState.isAuthenticated = true
  })

  it('adopts the authoritative payload when retry succeeds after a failed optimistic replay', async () => {
    const store = useWorkspaceStore()
    const replayed = makeGuide()
    store.onboarding = makeDeferredPlaceholder()
    vi.mocked(http.put)
      .mockRejectedValueOnce(new Error('first replay failed'))
      .mockResolvedValueOnce({ data: replayed })

    await expect(store.updateOnboarding('replay')).rejects.toThrow('first replay failed')
    expect(store.onboarding).toMatchObject({
      visibility: 'active',
      isComplete: false,
      currentStepId: null,
      steps: [],
    })

    await store.updateOnboarding('replay')

    expect(store.onboarding).toEqual(replayed)
  })

  it('lets the latest overlapping replay adopt the authoritative payload', async () => {
    const store = useWorkspaceStore()
    const firstResponse = createDeferred<{ data: WorkspaceOnboarding }>()
    const secondResponse = createDeferred<{ data: WorkspaceOnboarding }>()
    const replayed = makeGuide()
    store.onboarding = makeDeferredPlaceholder()
    vi.mocked(http.put)
      .mockReturnValueOnce(firstResponse.promise)
      .mockReturnValueOnce(secondResponse.promise)

    const firstReplay = store.updateOnboarding('replay')
    const secondReplay = store.updateOnboarding('replay')

    secondResponse.resolve({ data: replayed })
    await secondReplay
    expect(store.onboarding).toEqual(replayed)

    firstResponse.resolve({ data: makeDeferredPlaceholder() })
    await firstReplay
    expect(store.onboarding).toEqual(replayed)
  })

  it('does not replace an already populated guide with a replay response echo', async () => {
    const store = useWorkspaceStore()
    const localGuide = makeGuide()
    const responseEcho = makeGuide({
      currentStepId: 'stale-server-step',
      steps: [
        {
          ...activeStep,
          stepId: 'stale-server-step',
          title: 'Stale server step',
        },
      ],
    })
    store.onboarding = localGuide
    vi.mocked(http.put).mockResolvedValueOnce({ data: responseEcho })

    await store.updateOnboarding('replay')

    expect(store.onboarding).toEqual(localGuide)
  })
})
