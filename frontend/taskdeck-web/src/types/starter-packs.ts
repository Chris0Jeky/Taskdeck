export interface StarterPackCompatibility {
  minTaskdeckVersion: string
  maxTaskdeckVersion?: string | null
  requiredFeatures: string[]
}

export interface StarterPackLabel {
  name: string
  color: string
  description?: string | null
}

export interface StarterPackColumn {
  name: string
  position: number
  wipLimit?: number | null
}

export interface StarterPackTemplate {
  templateId: string
  title: string
  description?: string | null
  checklist: string[]
}

export interface StarterPackSeedCard {
  title: string
  description?: string | null
  columnName: string
  templateId?: string | null
  labels: string[]
}

export interface StarterPackManifest {
  schemaVersion: string
  packId: string
  displayName: string
  description?: string | null
  compatibility: StarterPackCompatibility
  tags: string[]
  labels: StarterPackLabel[]
  columns: StarterPackColumn[]
  templates: StarterPackTemplate[]
  seedCards: StarterPackSeedCard[]
}

export interface ApplyStarterPackDto {
  manifest: StarterPackManifest
  dryRun: boolean
}

export interface StarterPackApplyAction {
  entityType: string
  operation: string
  key: string
  reason: string
}

export interface StarterPackApplyConflict {
  code: string
  path: string
  message: string
  existingValue: string | null
  incomingValue: string | null
  severity?: 'blocking' | 'warning' | (string & {})
}

export interface StarterPackApplyResult {
  boardId: string
  packId: string
  dryRun: boolean
  applied: boolean
  actions: StarterPackApplyAction[]
  conflicts: StarterPackApplyConflict[]
  hasConflicts?: boolean
  hasBlockingConflicts?: boolean
}

export interface StarterPackCatalogEntry {
  id: string
  category: string
  title: string
  summary: string
  highlights: string[]
  manifest: StarterPackManifest
}
