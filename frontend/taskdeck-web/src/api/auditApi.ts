import http from './http'
import type { AuditEntry } from '../types/audit'

export const auditApi = {
  async getBoardHistory(boardId: string, limit = 50): Promise<AuditEntry[]> {
    const { data } = await http.get<AuditEntry[]>(`/audit/boards/${boardId}?limit=${limit}`)
    return data
  },

  async getEntityHistory(entityType: string, entityId: string, limit = 50): Promise<AuditEntry[]> {
    const { data } = await http.get<AuditEntry[]>(`/audit/entities/${entityType}/${entityId}?limit=${limit}`)
    return data
  },

  async getUserHistory(userId: string, limit = 50): Promise<AuditEntry[]> {
    const { data } = await http.get<AuditEntry[]>(`/audit/users/${userId}?limit=${limit}`)
    return data
  },
}
