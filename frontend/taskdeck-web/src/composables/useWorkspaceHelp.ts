import { computed, ref, watch } from 'vue'
import { WORKSPACE_HELP_DISMISSALS_STORAGE_KEY } from '../utils/storageKeys'
import { useSessionStore } from '../store/sessionStore'

export const workspaceHelpTopics = [
  'home',
  'today',
  'review',
  'inbox',
  'board',
  'calendar',
  'activity-selectors',
  'board-access-selectors',
  'saved-views',
] as const

export type WorkspaceHelpTopic = typeof workspaceHelpTopics[number]

type WorkspaceHelpDismissals = Partial<Record<WorkspaceHelpTopic, true>>

function storageKeyForUser(userId: string | null | undefined): string {
  return userId?.trim()
    ? `${WORKSPACE_HELP_DISMISSALS_STORAGE_KEY}:${userId.trim()}`
    : WORKSPACE_HELP_DISMISSALS_STORAGE_KEY
}

function readDismissalsFromKey(storageKey: string): WorkspaceHelpDismissals {
  const raw = localStorage.getItem(storageKey)
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

function writeDismissals(storageKey: string, dismissals: WorkspaceHelpDismissals) {
  if (Object.keys(dismissals).length === 0) {
    localStorage.removeItem(storageKey)
    return
  }

  localStorage.setItem(storageKey, JSON.stringify(dismissals))
}

function readDismissals(userId: string | null | undefined): WorkspaceHelpDismissals {
  const scopedStorageKey = storageKeyForUser(userId)
  const scopedDismissals = readDismissalsFromKey(scopedStorageKey)
  if (Object.keys(scopedDismissals).length > 0 || !userId?.trim()) {
    return scopedDismissals
  }

  const legacyDismissals = readDismissalsFromKey(WORKSPACE_HELP_DISMISSALS_STORAGE_KEY)
  if (Object.keys(legacyDismissals).length === 0) {
    return scopedDismissals
  }

  writeDismissals(scopedStorageKey, legacyDismissals)
  localStorage.removeItem(WORKSPACE_HELP_DISMISSALS_STORAGE_KEY)
  return legacyDismissals
}

export function useWorkspaceHelp(topic: WorkspaceHelpTopic) {
  const session = useSessionStore()
  const dismissals = ref<WorkspaceHelpDismissals>(readDismissals(session.userId))
  const isDismissed = computed(() => dismissals.value[topic] === true)
  const isVisible = computed(() => !isDismissed.value)

  watch(
    () => session.userId,
    (nextUserId) => {
      dismissals.value = readDismissals(nextUserId)
    },
  )

  function dismiss() {
    const nextDismissals = {
      ...dismissals.value,
      [topic]: true,
    }
    writeDismissals(storageKeyForUser(session.userId), nextDismissals)
    dismissals.value = nextDismissals
  }

  function replay() {
    const nextDismissals = {
      ...dismissals.value,
    }
    delete nextDismissals[topic]
    writeDismissals(storageKeyForUser(session.userId), nextDismissals)
    dismissals.value = nextDismissals
  }

  return {
    isDismissed,
    isVisible,
    dismiss,
    replay,
  }
}
