import http from './http'
import type { ApplyStarterPackDto, StarterPackApplyResult } from '../types/starter-packs'

export const starterPacksApi = {
  async applyStarterPack(boardId: string, request: ApplyStarterPackDto): Promise<StarterPackApplyResult> {
    const { data } = await http.post<StarterPackApplyResult>(`/boards/${boardId}/starter-packs/apply`, request)
    return data
  },
}
