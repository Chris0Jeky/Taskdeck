export interface BoardRealtimeEvent {
  boardId: string
  entityType: string
  operation: string
  entityId: string | null
  occurredAt: string
}

export interface BoardPresenceMember {
  userId: string
  displayName: string | null
  editingCardId: string | null
}

export interface BoardPresenceSnapshot {
  boardId: string
  members: BoardPresenceMember[]
  occurredAt: string
}
