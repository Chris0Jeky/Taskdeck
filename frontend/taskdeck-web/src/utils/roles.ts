import type { BoardRole, BoardRoleValue } from '../types/access'

const roleByValue: Record<number, BoardRole> = {
  0: 'Owner',
  1: 'Admin',
  2: 'Editor',
  3: 'Viewer',
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

  if (role in valueByRole) {
    return role
  }

  return 'Viewer'
}

export function toBoardRoleValue(role: BoardRoleValue): number {
  if (typeof role === 'number') {
    return role
  }
  return valueByRole[normalizeBoardRole(role)]
}
