export interface BoardRealtimeEvent {
  boardId: string
  entityType: string
  operation: string
  entityId: string | null
  occurredAt: string
}
