import { defineStore } from 'pinia'

export const workspaceExperiences = ['classic', 'studio', 'companion', 'unified'] as const
export const workspacePresentations = ['zen', 'studio', 'control'] as const
export type WorkspaceExperience = typeof workspaceExperiences[number]
export type WorkspacePresentation = typeof workspacePresentations[number]
const STORAGE_KEY = 'td.workspace.layout.v1'

function readPreferences(): { experience: WorkspaceExperience; presentation: WorkspacePresentation } {
  const fallback = { experience: 'classic' as const, presentation: 'studio' as const }
  try {
    const value: unknown = JSON.parse(window.localStorage.getItem(STORAGE_KEY) ?? 'null')
    if (!value || typeof value !== 'object') return fallback
    const saved = value as Record<string, unknown>
    return {
      experience: workspaceExperiences.includes(saved.experience as WorkspaceExperience)
        ? saved.experience as WorkspaceExperience : fallback.experience,
      presentation: workspacePresentations.includes(saved.presentation as WorkspacePresentation)
        ? saved.presentation as WorkspacePresentation : fallback.presentation,
    }
  } catch {
    return fallback
  }
}

/** Local display choices only: never workspace authority, navigation gates, or task state. */
export const useWorkspaceLayoutStore = defineStore('workspaceLayout', {
  state: readPreferences,
  actions: {
    setExperience(experience: WorkspaceExperience) {
      if (!workspaceExperiences.includes(experience)) return
      this.experience = experience
      this.persist()
    },
    setPresentation(presentation: WorkspacePresentation) {
      if (!workspacePresentations.includes(presentation)) return
      this.presentation = presentation
      this.persist()
    },
    persist() {
      try {
        window.localStorage.setItem(STORAGE_KEY, JSON.stringify({
          experience: this.experience, presentation: this.presentation,
        }))
      } catch {
        // Display remains usable when the browser cannot save preferences.
      }
    },
  },
})
