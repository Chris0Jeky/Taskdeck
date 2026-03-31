import http from './http'

export interface SearchBoardHit {
  id: string
  name: string
  description: string | null
  isArchived: boolean
}

export interface SearchCardHit {
  id: string
  boardId: string
  boardName: string
  columnId: string
  columnName: string
  title: string
  description: string
}

export interface GlobalSearchResult {
  boards: SearchBoardHit[]
  cards: SearchCardHit[]
  totalCardCount: number
  hasMoreCards: boolean
  offset: number
  maxResults: number
}

export const searchApi = {
  async search(
    query: string,
    signal?: AbortSignal,
    options?: { maxResults?: number; offset?: number },
  ): Promise<GlobalSearchResult> {
    const params = new URLSearchParams()
    params.append('q', query)
    if (options?.maxResults !== undefined) {
      params.append('maxResults', String(options.maxResults))
    }
    if (options?.offset !== undefined) {
      params.append('offset', String(options.offset))
    }
    const { data } = await http.get<GlobalSearchResult>(`/search?${params}`, { signal })
    return data
  },
}
