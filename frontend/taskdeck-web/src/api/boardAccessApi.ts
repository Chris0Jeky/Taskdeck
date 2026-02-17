import http from './http'
import type { BoardAccess, GrantAccessDto, UpdateAccessDto } from '../types/access'
import { normalizeBoardRole, toBoardRoleValue } from '../utils/roles'

function encodePathSegment(value: string): string {
  return encodeURIComponent(value)
}

function normalizeAccess(entry: BoardAccess): BoardAccess {
  return {
    ...entry,
    role: normalizeBoardRole(entry.role),
  }
}

export const boardAccessApi = {
  async getAccess(boardId: string): Promise<BoardAccess[]> {
    const pathBoardId = encodePathSegment(boardId)
    const { data } = await http.get<BoardAccess[]>(`/boards/${pathBoardId}/access`)
    return data.map(normalizeAccess)
  },

  async grantAccess(boardId: string, access: GrantAccessDto): Promise<BoardAccess> {
    const pathBoardId = encodePathSegment(boardId)
    const { data } = await http.post<BoardAccess>(`/boards/${pathBoardId}/access`, {
      ...access,
      role: toBoardRoleValue(access.role),
    })
    return normalizeAccess(data)
  },

  async updateAccess(boardId: string, accessId: string, access: UpdateAccessDto): Promise<BoardAccess> {
    const pathBoardId = encodePathSegment(boardId)
    const pathAccessId = encodePathSegment(accessId)
    const { data } = await http.put<BoardAccess>(`/boards/${pathBoardId}/access/${pathAccessId}`, {
      ...access,
      role: toBoardRoleValue(access.role),
    })
    return normalizeAccess(data)
  },

  async revokeAccess(boardId: string, accessId: string): Promise<void> {
    const pathBoardId = encodePathSegment(boardId)
    const pathAccessId = encodePathSegment(accessId)
    await http.delete(`/boards/${pathBoardId}/access/${pathAccessId}`)
  },
}
