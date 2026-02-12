export interface AuditEntry {
  id: string
  entityType: string
  entityId: string
  action: string
  actorId: string | null
  actorName: string | null
  details: string | null
  timestamp: string
}
