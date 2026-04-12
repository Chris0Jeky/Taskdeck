import http from './http'
import type {
  CalendarData,
  HomeSummary,
  TodaySummary,
  UpdateWorkspaceOnboardingDto,
  UpdateWorkspacePreferenceDto,
  WorkspaceOnboarding,
  WorkspacePreference,
} from '../types/workspace'

export const workspaceApi = {
  async getHomeSummary(): Promise<HomeSummary> {
    const { data } = await http.get<HomeSummary>('/workspace/home')
    return data
  },

  async getTodaySummary(): Promise<TodaySummary> {
    const { data } = await http.get<TodaySummary>('/workspace/today')
    return data
  },

  async getCalendar(from: string, to: string): Promise<CalendarData> {
    const params = new URLSearchParams({ from, to })
    const { data } = await http.get<CalendarData>(`/workspace/calendar?${params}`)
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

  async updateOnboarding(dto: UpdateWorkspaceOnboardingDto): Promise<WorkspaceOnboarding> {
    const { data } = await http.put<WorkspaceOnboarding>('/workspace/onboarding', dto)
    return data
  },
}
