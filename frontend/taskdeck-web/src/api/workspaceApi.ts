import http from './http'
import type { HomeSummary, UpdateWorkspacePreferenceDto, WorkspacePreference } from '../types/workspace'

export const workspaceApi = {
  async getHomeSummary(): Promise<HomeSummary> {
    const { data } = await http.get<HomeSummary>('/workspace/home')
    return data
  },

  async getPreferences(): Promise<WorkspacePreference> {
    const { data } = await http.get<WorkspacePreference>('/workspace/preferences')
    return data
  },

  async updatePreferences(dto: UpdateWorkspacePreferenceDto): Promise<WorkspacePreference> {
    const { data } = await http.put<WorkspacePreference>('/workspace/preferences', dto)
    return data
  },
}
