import { defineStore } from 'pinia'
import { computed, ref, watch } from 'vue'
import { useSessionStore } from './sessionStore'
import {
  isWorkspaceExperience,
  isWorkspacePresentation,
  type WorkspaceExperience,
  type WorkspacePresentation,
} from './workspaceLayoutStore'
import { frontendBuildIdentity } from '../utils/frontendBuildIdentity'
import { comparisonChecksum, comparisonId } from '../utils/comparisonIdentity'

export interface WorkspaceTrial {
  id: string
  experience: WorkspaceExperience
  presentation: WorkspacePresentation
  theme: string
  build: string | null
  frontendBuild: string | null
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

export type WorkspaceTrialInput = Omit<WorkspaceTrial, 'id' | 'recordedAt' | 'build' | 'frontendBuild'> & { build?: string | null }
export const MAX_COMPARISON_TRIALS = 500
export const MAX_COMPARISON_FILE_BYTES = 2 * 1024 * 1024

function isScenario(value: unknown): value is WorkspaceScenario {
  return WORKSPACE_COMPARISON_SCENARIOS.some(scenario => scenario.id === value)
}

function isCompletionOutcome(value: unknown): value is WorkspaceCompletionOutcome {
  return WORKSPACE_COMPLETION_OUTCOMES.some(outcome => outcome.id === value)
}

/** In-memory notes with explicit portable files. No automatic assignment or telemetry. */
export const useWorkspaceExperimentStore = defineStore('workspaceExperiment', () => {
  const session = useSessionStore()
  const trials = ref<WorkspaceTrial[]>([])
  const resetVersion = ref(0)
  watch(() => session.userId, () => { resetVersion.value++; trials.value = [] }, { flush: 'sync' })

  function record(trial: WorkspaceTrialInput) {
    const ease = trial.ease ?? null
    if (!session.userId || trials.value.length >= MAX_COMPARISON_TRIALS || !isScenario(trial.scenario) || !isCompletionOutcome(trial.completionOutcome)) return false
    if (ease !== null && (!Number.isInteger(ease) || ease < 1 || ease > 5)) return false
    if (!isWorkspaceExperience(trial.experience) || !isWorkspacePresentation(trial.presentation)) return false
    const build = typeof trial.build === 'string' && trial.build.trim() ? trial.build.trim().slice(0, 256) : null
    if (typeof trial.theme !== 'string' || !trial.theme || trial.theme.length > 64 || typeof trial.note !== 'string') return false
    trials.value.push({ ...trial, id: comparisonId(), frontendBuild: frontendBuildIdentity, build, ease, note: trial.note.slice(0, 2000), recordedAt: new Date().toISOString() })
    return true
  }

  function clear() { resetVersion.value++; trials.value = [] }
  function exportJson() {
    return JSON.stringify({ kind: 'taskdeck-workspace-comparison', version: 3, trials: trials.value }, null, 2)
  }

  async function importJson(json: string) {
    const request = resetVersion.value
    if (!session.userId) throw new Error('Sign in before importing observations.')
    if (new TextEncoder().encode(json).byteLength > MAX_COMPARISON_FILE_BYTES) throw new Error('Choose a comparison file smaller than 2 MiB.')
    const envelope: unknown = JSON.parse(json)
    if (!envelope || typeof envelope !== 'object') throw new Error('Choose a Taskdeck comparison export.')
    const data = envelope as Record<string, unknown>
    if (data.kind !== 'taskdeck-workspace-comparison' || ![2, 3].includes(data.version as number)
      || !Array.isArray(data.trials) || data.trials.length > MAX_COMPARISON_TRIALS)
      throw new Error('Choose a version 2 or 3 comparison file with at most 500 observations.')
    const incoming: WorkspaceTrial[] = []
    for (const item of data.trials) {
      const value = validateImportedTrial(item, data.version as number)
      if (data.version === 2) {
        value.id = `legacy-${await comparisonChecksum(JSON.stringify(value))}`
      }
      incoming.push(value)
    }
    if (request !== resetVersion.value || !session.userId) throw new Error('The session changed. Select the file again.')
    const merged = new Map(trials.value.map(trial => [trial.id, trial]))
    let added = 0
    for (const trial of incoming) {
      const existing = merged.get(trial.id)
      if (existing && JSON.stringify(validateImportedTrial(existing, 3)) !== JSON.stringify(trial)) throw new Error('The file has conflicting observation IDs. Nothing was imported.')
      if (!existing) { merged.set(trial.id, trial); added++ }
    }
    if (merged.size > MAX_COMPARISON_TRIALS) throw new Error('Keep at most 500 observations. Export and clear this session before importing more.')
    trials.value = [...merged.values()]
    return added
  }

  const groups = computed(() => {
    const result = new Map<string, { key: string; trial: WorkspaceTrial; count: number; completed: number; blocked: number; rated: number; easeTotal: number }>()
    for (const trial of trials.value) {
      const key = JSON.stringify([trial.scenario, trial.experience, trial.presentation, trial.theme, trial.build, trial.frontendBuild])
      const group = result.get(key) ?? { key, trial, count: 0, completed: 0, blocked: 0, rated: 0, easeTotal: 0 }
      group.count++
      if (trial.completionOutcome === 'completed') group.completed++
      if (trial.completionOutcome === 'blocked') group.blocked++
      if (trial.ease !== null) { group.rated++; group.easeTotal += trial.ease }
      result.set(key, group)
    }
    return [...result.values()]
  })

  return { trials, groups, resetVersion, record, clear, exportJson, importJson }
})

function validateImportedTrial(input: unknown, version: number): WorkspaceTrial {
  const bad = () => new Error('The file contains an invalid observation. Nothing was imported.')
  if (!input || typeof input !== 'object') throw bad()
  const x = input as Record<string, unknown>
  if (!isWorkspaceExperience(x.experience) || !isWorkspacePresentation(x.presentation)
    || typeof x.theme !== 'string' || !x.theme || x.theme.length > 64
    || !isScenario(x.scenario) || !isCompletionOutcome(x.completionOutcome)
    || (x.ease !== null && (!Number.isInteger(x.ease) || (x.ease as number) < 1 || (x.ease as number) > 5))
    || typeof x.note !== 'string' || x.note.length > 2000
    || typeof x.recordedAt !== 'string' || x.recordedAt.length > 40 || !Number.isFinite(Date.parse(x.recordedAt))
    || (x.build !== null && (typeof x.build !== 'string' || x.build.length > 256))) throw bad()
  if (version === 3 && (typeof x.id !== 'string' || !/^(?:[a-f0-9-]{36}|legacy-[a-f0-9]{64})$/.test(x.id)
    || (x.frontendBuild !== null && (typeof x.frontendBuild !== 'string' || !/^sha256:[a-f0-9]{64}$/.test(x.frontendBuild))))) throw bad()
  return { experience: x.experience, presentation: x.presentation, theme: x.theme, build: x.build as string | null,
    scenario: x.scenario, completionOutcome: x.completionOutcome, ease: x.ease as number | null,
    note: x.note, recordedAt: x.recordedAt, id: version === 3 ? x.id as string : '', frontendBuild: version === 3 ? x.frontendBuild as string | null : null }
}
