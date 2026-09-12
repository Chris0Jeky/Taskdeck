import http from './http'
import type { BoardEstimateRollup } from '../types/estimateRollups'

export const estimateRollupsApi = {
  async get(boardId: string): Promise<BoardEstimateRollup> {
    const { data } = await http.get<BoardEstimateRollup>(`/boards/${boardId}/estimate-rollups`)
    return data
  },
}
