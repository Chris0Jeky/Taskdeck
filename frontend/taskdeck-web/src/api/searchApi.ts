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
}

export const searchApi = {
  async search(query: string): Promise<GlobalSearchResult> {
    const params = new URLSearchParams()
    params.append('q', query)
    const { data } = await http.get<GlobalSearchResult>(`/search?${params}`)
    return data
  },
}
