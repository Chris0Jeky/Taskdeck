import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useWorkspaceStore } from '../store/workspaceStore'
import type { WorkspaceOnboardingStep } from '../types/workspace'

export function useWorkspaceOnboardingActions(refreshSummary: () => void) {
  const router = useRouter()
  const workspace = useWorkspaceStore()
  const showSetupModal = ref(false)

  function openRoute(route: string) {
    void router.push(route)
  }

  function openSetupModal() {
    showSetupModal.value = true
  }

  function closeSetupModal() {
    showSetupModal.value = false
  }

  function handleSetupCreated() {
    refreshSummary()
  }

  function openOnboardingStep(step: WorkspaceOnboardingStep) {
    if (step.targetSurface === 'boards') {
      openSetupModal()
      return
    }

    openRoute(step.targetSurface === 'review' ? '/workspace/review' : '/workspace/inbox')
  }

  async function dismissOnboarding() {
    try {
      await workspace.updateOnboarding('dismiss')
    } catch {
      // The store retains the warning state.
    }
  }

  async function replayOnboarding() {
    try {
      await workspace.updateOnboarding('replay')
    } catch {
      // The store retains the warning state.
    }
  }

  return {
    showSetupModal,
    openRoute,
    openSetupModal,
    closeSetupModal,
    handleSetupCreated,
    openOnboardingStep,
    dismissOnboarding,
    replayOnboarding,
  }
}
