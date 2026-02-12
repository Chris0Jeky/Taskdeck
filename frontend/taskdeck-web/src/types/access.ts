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

export interface GrantAccessDto {
  userId: string
  role: BoardRoleValue
}

export interface UpdateAccessDto {
  role: BoardRoleValue
}
