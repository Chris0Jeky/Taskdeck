export type BoardRole = 'Owner' | 'Admin' | 'Editor' | 'Viewer'
export type BoardRoleValue = BoardRole | number

export interface BoardAccess {
  id: string
  boardId: string
  userId: string
  role: BoardRoleValue
  grantedBy: string
  grantedAt: string
}

// Grant board access by an email-or-username `identifier` (resolved server-side) or, for API
// compatibility, a raw `userId` GUID. Supply exactly one; `identifier` takes precedence.
export interface GrantAccessDto {
  userId?: string
  identifier?: string
  role: BoardRoleValue
}

export interface UpdateAccessDto {
  role: BoardRoleValue
}
