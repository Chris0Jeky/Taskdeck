export type BoardRole = 'Owner' | 'Admin' | 'Editor' | 'Viewer'

export interface BoardAccess {
  id: string
  boardId: string
  userId: string
  role: BoardRole
  grantedBy: string
  grantedAt: string
  updatedAt: string
}

export interface GrantAccessDto {
  userId: string
  role: BoardRole
}

export interface UpdateAccessDto {
  role: BoardRole
}
