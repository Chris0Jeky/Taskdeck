import http from './http'
import type { ArchiveItem, RestoreArchiveRequest, RestoreArchiveResult } from '../types/archive'

function toQuery(filters?: { entityType?: string; boardId?: string; status?: string; limit?: number }): string {
  if (!filters) {
    return ''
  }

  const params = new URLSearchParams()
  if (filters.entityType) params.set('entityType', filters.entityType)
  if (filters.boardId) params.set('boardId', filters.boardId)
  if (filters.status) params.set('status', filters.status)
  if (filters.limit !== undefined) params.set('limit', String(filters.limit))

  const query = params.toString()
  return query.length > 0 ? `?${query}` : ''
}

export const archiveApi = {
  async getItems(filters?: { entityType?: string; boardId?: string; status?: string; limit?: number }): Promise<ArchiveItem[]> {
    const { data } = await http.get<ArchiveItem[]>(`/archive/items${toQuery(filters)}`)
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
