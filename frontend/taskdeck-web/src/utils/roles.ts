import type { BoardRole, BoardRoleValue } from '../types/access'

const roleByValue: Record<number, BoardRole> = {
  0: 'Owner',
  1: 'Admin',
  2: 'Editor',
  3: 'Viewer',
}

const roleByName: Record<string, BoardRole> = {
  owner: 'Owner',
  admin: 'Admin',
  editor: 'Editor',
  viewer: 'Viewer',
}

const valueByRole: Record<BoardRole, number> = {
  Owner: 0,
  Admin: 1,
  Editor: 2,
  Viewer: 3,
}

export function normalizeBoardRole(role: BoardRoleValue): BoardRole {
  if (typeof role === 'number') {
    return roleByValue[role] ?? 'Viewer'
  }

  const normalized = role.trim().toLowerCase()
  return roleByName[normalized] ?? 'Viewer'
}

export function toBoardRoleValue(role: BoardRoleValue): number {
  if (typeof role === 'number') {
    return role
  }
  return valueByRole[normalizeBoardRole(role)]
}
