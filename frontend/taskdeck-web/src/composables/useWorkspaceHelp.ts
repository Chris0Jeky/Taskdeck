import { computed, ref } from 'vue'
import { WORKSPACE_HELP_DISMISSALS_STORAGE_KEY } from '../utils/storageKeys'

export const workspaceHelpTopics = ['home', 'today', 'review', 'inbox', 'board', 'selectors'] as const

export type WorkspaceHelpTopic = typeof workspaceHelpTopics[number]

type WorkspaceHelpDismissals = Partial<Record<WorkspaceHelpTopic, true>>

function readDismissals(): WorkspaceHelpDismissals {
  const raw = localStorage.getItem(WORKSPACE_HELP_DISMISSALS_STORAGE_KEY)
  if (!raw) {
    return {}
  }

  try {
    const parsed = JSON.parse(raw) as Record<string, unknown>
    const dismissals: WorkspaceHelpDismissals = {}

    for (const topic of workspaceHelpTopics) {
      if (parsed[topic] === true) {
        dismissals[topic] = true
      }
    }

    return dismissals
  } catch {
    return {}
  }
}

function writeDismissals(dismissals: WorkspaceHelpDismissals) {
  if (Object.keys(dismissals).length === 0) {
    localStorage.removeItem(WORKSPACE_HELP_DISMISSALS_STORAGE_KEY)
    return
  }

  localStorage.setItem(WORKSPACE_HELP_DISMISSALS_STORAGE_KEY, JSON.stringify(dismissals))
}

export function useWorkspaceHelp(topic: WorkspaceHelpTopic) {
  const isDismissed = ref(readDismissals()[topic] === true)
  const isVisible = computed(() => !isDismissed.value)

  function dismiss() {
    const nextDismissals = readDismissals()
    nextDismissals[topic] = true
    writeDismissals(nextDismissals)
    isDismissed.value = true
  }

  function replay() {
    const nextDismissals = readDismissals()
    delete nextDismissals[topic]
    writeDismissals(nextDismissals)
    isDismissed.value = false
  }

  return {
    isDismissed,
    isVisible,
    dismiss,
    replay,
  }
}
