import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  COLLABORATION_REFRESH_THROTTLE_MS,
  useWorkspaceCollaboration,
} from '../../composables/useWorkspaceCollaboration'

const mocks = vi.hoisted(() => ({
  getCollaboration: vi.fn(),
}))

vi.mock('../../api/workspaceApi', () => ({
  workspaceApi: { getCollaboration: mocks.getCollaboration },
}))

/**
 * `happy-dom` reports `visibilityState: 'visible'` and offers no setter, so the
 * property is redefined per spec and restored afterwards.
 */
function setVisibility(value: DocumentVisibilityState) {
  Object.defineProperty(document, 'visibilityState', {
    configurable: true,
    get: () => value,
  })
}

function fireVisibilityChange(value: DocumentVisibilityState) {
  setVisibility(value)
  document.dispatchEvent(new Event('visibilitychange'))
}

describe('useWorkspaceCollaboration', () => {
  beforeEach(() => {
    mocks.getCollaboration.mockReset()
    setVisibility('visible')
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('starts idle and unknown, so no consumer can act on a value it has not got', () => {
    const collaboration = useWorkspaceCollaboration()

    expect(collaboration.state.value).toBe('idle')
    expect(collaboration.memberCount.value).toBeNull()
    expect(collaboration.hasCollaborators.value).toBeNull()
    expect(collaboration.isMembershipKnown.value).toBe(false)
    expect(collaboration.isSoloWorkspace.value).toBe(false)
  })

  it('reports loading while the request is in flight and never claims solo mid-flight', async () => {
    let resolveRequest: ((value: unknown) => void) | undefined
    mocks.getCollaboration.mockReturnValueOnce(
      new Promise((resolve) => {
        resolveRequest = resolve
      }),
    )

    const collaboration = useWorkspaceCollaboration()
    const pending = collaboration.refresh()

    expect(collaboration.state.value).toBe('loading')
    expect(collaboration.isSoloWorkspace.value).toBe(false)
    expect(collaboration.isMembershipKnown.value).toBe(false)

    resolveRequest?.({ memberCount: 1, hasCollaborators: false })
    await pending

    expect(collaboration.state.value).toBe('ready')
    expect(collaboration.isSoloWorkspace.value).toBe(true)
  })

  it('reports a solo workspace only from a positive single-member answer', async () => {
    mocks.getCollaboration.mockResolvedValueOnce({ memberCount: 1, hasCollaborators: false })

    const collaboration = useWorkspaceCollaboration()
    await collaboration.refresh()

    expect(collaboration.state.value).toBe('ready')
    expect(collaboration.memberCount.value).toBe(1)
    expect(collaboration.hasCollaborators.value).toBe(false)
    expect(collaboration.isSoloWorkspace.value).toBe(true)
  })

  it('reports collaborators when the server counts more than one member', async () => {
    mocks.getCollaboration.mockResolvedValueOnce({ memberCount: 3, hasCollaborators: true })

    const collaboration = useWorkspaceCollaboration()
    await collaboration.refresh()

    expect(collaboration.memberCount.value).toBe(3)
    expect(collaboration.isSoloWorkspace.value).toBe(false)
    expect(collaboration.isMembershipKnown.value).toBe(true)
  })

  it('falls back to unknown when the request fails', async () => {
    mocks.getCollaboration.mockRejectedValueOnce(new Error('offline'))

    const collaboration = useWorkspaceCollaboration()
    await collaboration.refresh()

    expect(collaboration.state.value).toBe('unavailable')
    expect(collaboration.hasCollaborators.value).toBeNull()
    expect(collaboration.isSoloWorkspace.value).toBe(false)
  })

  it('falls back to unknown when the payload is not the expected shape', async () => {
    mocks.getCollaboration.mockResolvedValueOnce({ memberCount: '1', hasCollaborators: 'no' })

    const collaboration = useWorkspaceCollaboration()
    await collaboration.refresh()

    expect(collaboration.state.value).toBe('unavailable')
    expect(collaboration.isSoloWorkspace.value).toBe(false)
  })

  it('drops a previously known solo answer when a later refresh fails', async () => {
    mocks.getCollaboration.mockResolvedValueOnce({ memberCount: 1, hasCollaborators: false })
    mocks.getCollaboration.mockRejectedValueOnce(new Error('offline'))

    const collaboration = useWorkspaceCollaboration()
    await collaboration.refresh()
    expect(collaboration.isSoloWorkspace.value).toBe(true)

    await collaboration.refresh()

    expect(collaboration.state.value).toBe('unavailable')
    expect(collaboration.isSoloWorkspace.value).toBe(false)
  })

  it('coalesces concurrent refreshes into one request', async () => {
    mocks.getCollaboration.mockResolvedValue({ memberCount: 2, hasCollaborators: true })

    const collaboration = useWorkspaceCollaboration()
    await Promise.all([collaboration.refresh(), collaboration.refresh()])

    expect(mocks.getCollaboration).toHaveBeenCalledTimes(1)
  })

  it('refreshes when the document becomes visible again after the throttle window', async () => {
    vi.useFakeTimers()
    mocks.getCollaboration.mockResolvedValue({ memberCount: 1, hasCollaborators: false })

    const collaboration = useWorkspaceCollaboration()
    await collaboration.start()
    expect(mocks.getCollaboration).toHaveBeenCalledTimes(1)

    fireVisibilityChange('hidden')
    expect(mocks.getCollaboration).toHaveBeenCalledTimes(1)

    // Still inside the throttle window: a tab flip must not re-ask.
    fireVisibilityChange('visible')
    expect(mocks.getCollaboration).toHaveBeenCalledTimes(1)

    vi.advanceTimersByTime(COLLABORATION_REFRESH_THROTTLE_MS + 1)
    mocks.getCollaboration.mockResolvedValue({ memberCount: 2, hasCollaborators: true })
    fireVisibilityChange('visible')
    await vi.runAllTimersAsync()

    expect(mocks.getCollaboration).toHaveBeenCalledTimes(2)
    expect(collaboration.hasCollaborators.value).toBe(true)

    collaboration.stop()
  })

  it('stops listening once stopped', async () => {
    vi.useFakeTimers()
    mocks.getCollaboration.mockResolvedValue({ memberCount: 1, hasCollaborators: false })

    const collaboration = useWorkspaceCollaboration()
    await collaboration.start()
    collaboration.stop()

    vi.advanceTimersByTime(COLLABORATION_REFRESH_THROTTLE_MS + 1)
    fireVisibilityChange('visible')
    await vi.runAllTimersAsync()

    expect(mocks.getCollaboration).toHaveBeenCalledTimes(1)
  })
})
