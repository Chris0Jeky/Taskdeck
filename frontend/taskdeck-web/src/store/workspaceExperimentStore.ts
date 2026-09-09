import { defineStore } from 'pinia'
import { ref, watch } from 'vue'
import { useSessionStore } from './sessionStore'

export interface WorkspaceTrial {
  experience: string
  presentation: string
  theme: string
  recordedAt: string
  ease: number
  note: string
}

/** Deliberately session-only. No automatic assignment, telemetry or persisted work content. */
export const useWorkspaceExperimentStore = defineStore('workspaceExperiment', () => {
  const session = useSessionStore()
  const trials = ref<WorkspaceTrial[]>([])
  watch(() => session.userId, () => { trials.value = [] }, { flush: 'sync' })

  function record(trial: Omit<WorkspaceTrial, 'recordedAt'>) {
    if (!session.userId || !Number.isInteger(trial.ease) || trial.ease < 1 || trial.ease > 5) return false
    if (!['classic', 'studio', 'companion', 'unified'].includes(trial.experience) ||
        !['zen', 'studio', 'control'].includes(trial.presentation)) return false
    trials.value.push({ ...trial, note: trial.note.slice(0, 2000), recordedAt: new Date().toISOString() })
    return true
  }

  function clear() { trials.value = [] }
  function exportJson() {
    return JSON.stringify({ kind: 'taskdeck-workspace-comparison', version: 1, trials: trials.value }, null, 2)
  }

  return { trials, record, clear, exportJson }
})
