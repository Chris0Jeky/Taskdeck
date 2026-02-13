import http from './http'
import { buildQueryParams } from './queryBuilder'
import type { ArchiveItem, RestoreArchiveRequest, RestoreArchiveResult } from '../types/archive'

export const archiveApi = {
  async getItems(filters?: { entityType?: string; boardId?: string; status?: string; limit?: number }): Promise<ArchiveItem[]> {
    const params = buildQueryParams(filters)
    const query = params.toString()
    const { data } = await http.get<ArchiveItem[]>(`/archive/items${query.length > 0 ? `?${query}` : ''}`)
    return data
  },

  async restoreItem(entityType: string, entityId: string, request: RestoreArchiveRequest): Promise<RestoreArchiveResult> {
    const pathEntityType = encodeURIComponent(entityType)
    const pathEntityId = encodeURIComponent(entityId)
    const { data } = await http.post<RestoreArchiveResult>(
      `/archive/${pathEntityType}/${pathEntityId}/restore`,
      request
    )
    return data
  },
}
