import { describe, expect, it, beforeEach, vi } from 'vitest'
import type { WorkspaceOnboardingStep } from '../../types/workspace'

const routerMocks = vi.hoisted(() => ({
  push: vi.fn(),
}))

const workspaceMocks = vi.hoisted(() => ({
  updateOnboarding: vi.fn(),
}))

vi.mock('vue-router', () => ({
  useRouter: () => routerMocks,
}))

vi.mock('../../store/workspaceStore', () => ({
  useWorkspaceStore: () => workspaceMocks,
}))

async function loadComposable() {
  vi.resetModules()
  return import('../../composables/useWorkspaceOnboardingActions')
}

describe('useWorkspaceOnboardingActions', () => {
  beforeEach(() => {
    routerMocks.push.mockReset()
    workspaceMocks.updateOnboarding.mockReset()
  })

  it('opens routes and toggles the setup modal state', async () => {
    const refreshSummary = vi.fn()
    const { useWorkspaceOnboardingActions } = await loadComposable()
    const actions = useWorkspaceOnboardingActions(refreshSummary)

    expect(actions.showSetupModal.value).toBe(false)

    actions.openRoute('/workspace/review')
    expect(routerMocks.push).toHaveBeenCalledWith('/workspace/review')

    actions.openSetupModal()
    expect(actions.showSetupModal.value).toBe(true)

    actions.closeSetupModal()
    expect(actions.showSetupModal.value).toBe(false)

    actions.handleSetupCreated()
    expect(refreshSummary).toHaveBeenCalledTimes(1)
  })

  it('routes onboarding steps to the expected surface', async () => {
    const { useWorkspaceOnboardingActions } = await loadComposable()
    const actions = useWorkspaceOnboardingActions(vi.fn())

    actions.openOnboardingStep({ targetSurface: 'boards' } as WorkspaceOnboardingStep)
    expect(actions.showSetupModal.value).toBe(true)
    expect(routerMocks.push).not.toHaveBeenCalled()

    actions.closeSetupModal()

    actions.openOnboardingStep({ targetSurface: 'review' } as WorkspaceOnboardingStep)
    actions.openOnboardingStep({ targetSurface: 'capture' } as WorkspaceOnboardingStep)

    expect(routerMocks.push).toHaveBeenNthCalledWith(1, '/workspace/review')
    expect(routerMocks.push).toHaveBeenNthCalledWith(2, '/workspace/inbox')
  })

  it('updates onboarding visibility and swallows store failures', async () => {
    const { useWorkspaceOnboardingActions } = await loadComposable()
    const actions = useWorkspaceOnboardingActions(vi.fn())

    workspaceMocks.updateOnboarding.mockResolvedValueOnce(undefined)
    await actions.dismissOnboarding()
    expect(workspaceMocks.updateOnboarding).toHaveBeenNthCalledWith(1, 'dismiss')

    workspaceMocks.updateOnboarding.mockRejectedValueOnce(new Error('dismiss failed'))
    await expect(actions.dismissOnboarding()).resolves.toBeUndefined()

    workspaceMocks.updateOnboarding.mockResolvedValueOnce(undefined)
    await actions.replayOnboarding()
    expect(workspaceMocks.updateOnboarding).toHaveBeenNthCalledWith(3, 'replay')

    workspaceMocks.updateOnboarding.mockRejectedValueOnce(new Error('replay failed'))
    await expect(actions.replayOnboarding()).resolves.toBeUndefined()
    expect(workspaceMocks.updateOnboarding).toHaveBeenNthCalledWith(4, 'replay')
  })
})
