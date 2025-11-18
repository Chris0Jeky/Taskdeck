export interface Board {
  id: string
  name: string
  description: string | null
  isArchived: boolean
  createdAt: string
  updatedAt: string
}

export interface BoardDetail extends Board {
  columns: Column[]
}

export interface Column {
  id: string
  boardId: string
  name: string
  position: number
  wipLimit: number | null
  cardCount: number
  createdAt: string
  updatedAt: string
}

export interface Card {
  id: string
  boardId: string
  columnId: string
  title: string
  description: string
  dueDate: string | null
  isBlocked: boolean
  blockReason: string | null
  position: number
  labels: Label[]
  createdAt: string
  updatedAt: string
}

export interface Label {
  id: string
  boardId: string
  name: string
  colorHex: string
  createdAt: string
  updatedAt: string
}

// DTOs for creating/updating
export interface CreateBoardDto {
  name: string
  description?: string | null
}

export interface UpdateBoardDto {
  name?: string | null
  description?: string | null
  isArchived?: boolean | null
}

export interface CreateColumnDto {
  name: string
  position?: number | null
  wipLimit?: number | null
}

export interface UpdateColumnDto {
  name?: string | null
  position?: number | null
  wipLimit?: number | null
}

export interface CreateCardDto {
  columnId: string
  title: string
  description?: string | null
  dueDate?: string | null
  labelIds?: string[] | null
}

export interface UpdateCardDto {
  title?: string | null
  description?: string | null
  dueDate?: string | null
  isBlocked?: boolean | null
  blockReason?: string | null
  labelIds?: string[] | null
}

export interface MoveCardDto {
  targetColumnId: string
  targetPosition: number
}

export interface CreateLabelDto {
  name: string
  colorHex: string
}

export interface UpdateLabelDto {
  name?: string | null
  colorHex?: string | null
}
