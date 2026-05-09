import http from './http'

export interface CadenceBucket {
  hour: number
  eventCount: number
}

export interface CadenceApiResponse {
  buckets: CadenceBucket[]
  firstActionAt: string | null
  peakHour: number | null
  lastActionAt: string | null
}

export interface StreakDay {
  date: string
  isSealed: boolean
  intensityBucket: number
}

export interface StreakApiResponse {
  days: StreakDay[]
  currentStreakLength: number
  longestStreakLength: number
  dayCount: number
}

export interface SealApiResponse {
  sealedAt: string
  wasAlreadySealed: boolean
}

export interface SealStatusApiResponse {
  date: string
  isSealed: boolean
  sealedAt: string | null
}

export interface TomorrowNoteApiResponse {
  id: string
  date: string
  text: string
  updatedAt: string
  createdAt: string
}

export const todayApi = {
  async getCadence(date: string): Promise<CadenceApiResponse> {
    const { data } = await http.get<CadenceApiResponse>(`/today/cadence?date=${encodeURIComponent(date)}`)
    return data
  },

  async getStreak(days = 90): Promise<StreakApiResponse> {
    const { data } = await http.get<StreakApiResponse>(`/today/streak?days=${days}`)
    return data
  },

  async sealDay(date: string): Promise<SealApiResponse> {
    const { data } = await http.post<SealApiResponse>('/today/seal', { date })
    return data
  },

  async getSealStatus(date: string): Promise<SealStatusApiResponse> {
    const { data } = await http.get<SealStatusApiResponse>(`/today/seal?date=${encodeURIComponent(date)}`)
    return data
  },

  async getTomorrowNote(date: string): Promise<TomorrowNoteApiResponse | null> {
    const response = await http.get<TomorrowNoteApiResponse>(`/today/tomorrow-note?date=${encodeURIComponent(date)}`, {
      validateStatus: (status: number) => status === 200 || status === 204,
    })
    if (response.status === 204) return null
    return response.data
  },

  async saveTomorrowNote(date: string, text: string): Promise<TomorrowNoteApiResponse> {
    const { data } = await http.put<TomorrowNoteApiResponse>('/today/tomorrow-note', { date, text })
    return data
  },
}
