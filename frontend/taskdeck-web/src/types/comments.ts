export interface CardCommentMention {
  userId: string
  username: string
}

export interface CardComment {
  id: string
  boardId: string
  cardId: string
  parentCommentId: string | null
  authorUserId: string
  authorUsername: string
  content: string
  isDeleted: boolean
  editedAt: string | null
  mentions: CardCommentMention[]
  createdAt: string
  updatedAt: string
}

export interface CreateCardCommentDto {
  content: string
  parentCommentId?: string | null
}

export interface UpdateCardCommentDto {
  content: string
}
