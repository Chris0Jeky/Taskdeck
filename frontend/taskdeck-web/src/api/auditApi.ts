import http from './http'
import type { AuditEntry } from '../types/audit'

export const auditApi = {
  async getBoardHistory(boardId: string, limit = 50): Promise<AuditEntry[]> {
    const queryBoardId = encodeURIComponent(boardId)
    const { data } = await http.get<AuditEntry[]>(`/audit/boards/${queryBoardId}?limit=${limit}`)
    return data
  },

  async getEntityHistory(entityType: string, entityId: string, limit = 50): Promise<AuditEntry[]> {
    const queryEntityType = encodeURIComponent(entityType)
    const queryEntityId = encodeURIComponent(entityId)
    const { data } = await http.get<AuditEntry[]>(`/audit/entities/${queryEntityType}/${queryEntityId}?limit=${limit}`)
    return data
  },

  async getUserHistory(limit = 50): Promise<AuditEntry[]> {
    const { data } = await http.get<AuditEntry[]>(`/audit/users/me?limit=${limit}`)
    return data
  },
}
