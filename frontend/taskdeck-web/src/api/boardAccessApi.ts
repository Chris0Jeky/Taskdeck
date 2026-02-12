import http from './http'
import type { BoardAccess, GrantAccessDto, UpdateAccessDto } from '../types/access'
import { normalizeBoardRole, toBoardRoleValue } from '../utils/roles'

function normalizeAccess(entry: BoardAccess): BoardAccess {
  return {
    ...entry,
    role: normalizeBoardRole(entry.role),
  }
}

export const boardAccessApi = {
  async getAccess(boardId: string): Promise<BoardAccess[]> {
    const { data } = await http.get<BoardAccess[]>(`/boards/${boardId}/access`)
    return data.map(normalizeAccess)
  },

  async grantAccess(boardId: string, access: GrantAccessDto, grantedBy: string): Promise<BoardAccess> {
    const queryGrantedBy = encodeURIComponent(grantedBy)
    const { data } = await http.post<BoardAccess>(`/boards/${boardId}/access?grantedBy=${queryGrantedBy}`, {
      ...access,
      role: toBoardRoleValue(access.role),
    })
    return normalizeAccess(data)
  },

  async updateAccess(boardId: string, accessId: string, access: UpdateAccessDto, updatedBy: string): Promise<BoardAccess> {
    const queryUpdatedBy = encodeURIComponent(updatedBy)
    const { data } = await http.put<BoardAccess>(`/boards/${boardId}/access/${accessId}?updatedBy=${queryUpdatedBy}`, {
      ...access,
      role: toBoardRoleValue(access.role),
    })
    return normalizeAccess(data)
  },

  async revokeAccess(boardId: string, accessId: string, revokedBy: string): Promise<void> {
    const queryRevokedBy = encodeURIComponent(revokedBy)
    await http.delete(`/boards/${boardId}/access/${accessId}?revokedBy=${queryRevokedBy}`)
  },
}
