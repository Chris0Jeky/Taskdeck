export interface AuditEntry {
  id: string
  entityType: string
  entityId: string
  action: string | number
  userId: string | null
  userName: string | null
  changes: string | null
  timestamp: string
}
