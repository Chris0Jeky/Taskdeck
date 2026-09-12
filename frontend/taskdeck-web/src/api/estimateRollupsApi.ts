import http, { BOARD_REQUEST_TIMEOUT_MS } from './http'
import type { BoardEstimateRollup } from '../types/estimateRollups'

export const estimateRollupsApi = {
  async get(boardId: string, options: { signal?: AbortSignal } = {}): Promise<BoardEstimateRollup> {
    const { data } = await http.get<BoardEstimateRollup>(`/boards/${boardId}/estimate-rollups`, {
      signal: options.signal,
      timeout: BOARD_REQUEST_TIMEOUT_MS,
      skipRetry: true,
    })
    return data
  },
}
