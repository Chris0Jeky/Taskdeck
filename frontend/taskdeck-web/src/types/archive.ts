export type RestoreStatus = 'Available' | 'Restored' | 'Expired' | 'Conflict'
export type RestoreStatusValue = RestoreStatus | number

export interface ArchiveItem {
  id: string
  entityType: string
  entityId: string
  boardId: string
  name: string
  archivedByUserId: string
  archivedAt: string
  reason: string | null
  restoreStatus: RestoreStatusValue
  restoredAt: string | null
  restoredByUserId: string | null
  createdAt: string
  updatedAt: string
}

export interface RestoreArchiveRequest {
  targetBoardId: string | null
  restoreMode: number
  conflictStrategy: number
}

export interface RestoreArchiveResult {
  success: boolean
  restoredEntityId: string | null
  errorMessage: string | null
  resolvedName: string | null
}
