import http from './http'
import type { BoardAccess, GrantAccessDto, UpdateAccessDto } from '../types/access'

export const boardAccessApi = {
  async getAccess(boardId: string): Promise<BoardAccess[]> {
    const { data } = await http.get<BoardAccess[]>(`/boards/${boardId}/access`)
    return data
  },

  async grantAccess(boardId: string, access: GrantAccessDto, grantedBy: string): Promise<BoardAccess> {
    const { data } = await http.post<BoardAccess>(`/boards/${boardId}/access?grantedBy=${grantedBy}`, access)
    return data
  },

  async updateAccess(boardId: string, accessId: string, access: UpdateAccessDto, updatedBy: string): Promise<BoardAccess> {
    const { data } = await http.put<BoardAccess>(`/boards/${boardId}/access/${accessId}?updatedBy=${updatedBy}`, access)
    return data
  },

  async revokeAccess(boardId: string, accessId: string, revokedBy: string): Promise<void> {
    await http.delete(`/boards/${boardId}/access/${accessId}?revokedBy=${revokedBy}`)
  },
}
