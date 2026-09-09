import { defineStore } from 'pinia'
import { ref, watch } from 'vue'
import { useSessionStore } from './sessionStore'

export interface WorkspaceTrial {
  experience: string
  presentation: string
  theme: string
  build: string | null
  scenario: WorkspaceScenario
  completionOutcome: WorkspaceCompletionOutcome
  recordedAt: string
  ease: number | null
  note: string
}

export const WORKSPACE_COMPARISON_SCENARIOS = [
  { id: 'capture-review-board', label: 'Capture → Review → Board' },
  { id: 'resume-thinking', label: 'Resume a card and leave a next step' },
  { id: 'insight-memory', label: 'Check insights and answer a Memory question' },
] as const

export type WorkspaceScenario = typeof WORKSPACE_COMPARISON_SCENARIOS[number]['id']

export const WORKSPACE_COMPLETION_OUTCOMES = [
  { id: 'completed', label: 'Completed the scenario' },
  { id: 'stopped', label: 'Stopped with a next step' },
  { id: 'blocked', label: 'Reached a blocker' },
  { id: 'not-completed', label: 'Did not complete the scenario' },
] as const

export type WorkspaceCompletionOutcome = typeof WORKSPACE_COMPLETION_OUTCOMES[number]['id']

export type WorkspaceTrialInput = Omit<WorkspaceTrial, 'recordedAt' | 'build'> & { build?: string | null }

function isScenario(value: unknown): value is WorkspaceScenario {
  return WORKSPACE_COMPARISON_SCENARIOS.some(scenario => scenario.id === value)
}

function isCompletionOutcome(value: unknown): value is WorkspaceCompletionOutcome {
  return WORKSPACE_COMPLETION_OUTCOMES.some(outcome => outcome.id === value)
}

/** Deliberately session-only. No automatic assignment, telemetry or persisted work content. */
export const useWorkspaceExperimentStore = defineStore('workspaceExperiment', () => {
  const session = useSessionStore()
  const trials = ref<WorkspaceTrial[]>([])
  watch(() => session.userId, () => { trials.value = [] }, { flush: 'sync' })

  function record(trial: WorkspaceTrialInput) {
    const ease = trial.ease ?? null
    if (!session.userId || !isScenario(trial.scenario) || !isCompletionOutcome(trial.completionOutcome)) return false
    if (ease !== null && (!Number.isInteger(ease) || ease < 1 || ease > 5)) return false
    if (!['classic', 'studio', 'companion', 'unified'].includes(trial.experience) ||
        !['zen', 'studio', 'control'].includes(trial.presentation)) return false
    const build = typeof trial.build === 'string' && trial.build.trim() ? trial.build.trim().slice(0, 256) : null
    trials.value.push({ ...trial, build, ease, note: trial.note.slice(0, 2000), recordedAt: new Date().toISOString() })
    return true
  }

  function clear() { trials.value = [] }
  function exportJson() {
    return JSON.stringify({ kind: 'taskdeck-workspace-comparison', version: 2, trials: trials.value }, null, 2)
  }

  return { trials, record, clear, exportJson }
})
