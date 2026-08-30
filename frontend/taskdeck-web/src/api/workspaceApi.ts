import http from './http'
import type {
  CalendarData,
  HomeSummary,
  TodaySummary,
  UpdateWorkspaceOnboardingDto,
  UpdateWorkspacePreferenceDto,
  WorkspaceCollaboration,
  WorkspaceOnboarding,
  WorkspacePreference,
} from '../types/workspace'
import { localCalendarDateKey } from '../utils/dueDates'

export const workspaceApi = {
  async getHomeSummary(): Promise<HomeSummary> {
    const { data } = await http.get<HomeSummary>('/workspace/home')
    return data
  },

  async getTodaySummary(localDate: string = localCalendarDateKey()): Promise<TodaySummary> {
    const params = new URLSearchParams({ localDate })
    const { data } = await http.get<TodaySummary>(`/workspace/today?${params}`)
    return data
  },

  async getCalendar(
    from: string,
    to: string,
    localDate: string = localCalendarDateKey(),
  ): Promise<CalendarData> {
    const params = new URLSearchParams({ from, to, localDate })
    const { data } = await http.get<CalendarData>(`/workspace/calendar?${params}`)
    return data
  },

  async getCollaboration(): Promise<WorkspaceCollaboration> {
    const { data } = await http.get<WorkspaceCollaboration>('/workspace/collaboration')
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
