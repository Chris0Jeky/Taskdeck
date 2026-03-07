export const workspaceModes = ['guided', 'workbench', 'agent'] as const

export type WorkspaceMode = typeof workspaceModes[number]

export function isWorkspaceMode(value: string | null | undefined): value is WorkspaceMode {
  return workspaceModes.includes(value as WorkspaceMode)
}

export interface WorkspacePreference {
  userId: string
  workspaceMode: WorkspaceMode
  createdAt: string
  updatedAt: string
}

export interface UpdateWorkspacePreferenceDto {
  workspaceMode: WorkspaceMode
}

export interface HomeWorkloadSummary {
  capturesNeedingTriage: number
  capturesInProgress: number
  capturesReadyForFollowUp: number
  proposalsPendingReview: number
}

export interface HomeRecommendedAction {
  actionId: string
  title: string
  description: string
  targetSurface: 'capture' | 'review' | 'boards' | 'board'
  boardId?: string | null
  attentionCount?: number | null
}

export interface HomeRecentBoard {
  id: string
  name: string
  description: string | null
  updatedAt: string
}

export interface HomeBoardSummary {
  totalBoards: number
  recentBoardsCount: number
  recentBoards: HomeRecentBoard[]
}

export interface HomeSummary {
  workspaceMode: WorkspaceMode
  isFirstRun: boolean
  workload: HomeWorkloadSummary
  boards: HomeBoardSummary
  recommendedActions: HomeRecommendedAction[]
}
