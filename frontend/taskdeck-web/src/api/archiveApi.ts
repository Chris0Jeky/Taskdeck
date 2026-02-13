import http from './http'
import type { ArchiveItem, RestoreArchiveRequest, RestoreArchiveResult } from '../types/archive'
import { buildQueryString } from '../utils/queryBuilder'

export const archiveApi = {
  async getItems(filters?: { entityType?: string; boardId?: string; status?: string; limit?: number }): Promise<ArchiveItem[]> {
    const { data } = await http.get<ArchiveItem[]>(`/archive/items${buildQueryString(filters)}`)
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
