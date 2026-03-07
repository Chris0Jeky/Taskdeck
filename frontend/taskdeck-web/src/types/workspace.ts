export type WorkspaceMode = 'guided' | 'workbench' | 'agent'
export type WorkspaceOnboardingVisibility = 'active' | 'dismissed'
export type WorkspaceOnboardingAction = 'dismiss' | 'replay'
export type WorkspaceSurface = 'capture' | 'review' | 'boards' | 'board'

export interface WorkspaceOnboardingStep {
  stepId: string
  title: string
  description: string
  targetSurface: WorkspaceSurface
  isComplete: boolean
}

export interface WorkspaceOnboarding {
  visibility: WorkspaceOnboardingVisibility
  isComplete: boolean
  currentStepId: string | null
  dismissedAt: string | null
  completedAt: string | null
  steps: WorkspaceOnboardingStep[]
}

export interface WorkspacePreference {
  userId: string
  workspaceMode: WorkspaceMode
  onboarding: WorkspaceOnboarding
  createdAt: string
  updatedAt: string
}

export interface UpdateWorkspacePreferenceDto {
  workspaceMode: WorkspaceMode
}

export interface UpdateWorkspaceOnboardingDto {
  action: WorkspaceOnboardingAction
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
  targetSurface: WorkspaceSurface
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
  onboarding: WorkspaceOnboarding
  workload: HomeWorkloadSummary
  boards: HomeBoardSummary
  recommendedActions: HomeRecommendedAction[]
}

export interface TodayAgendaSummary {
  capturesNeedingTriage: number
  proposalsPendingReview: number
  overdueCards: number
  dueTodayCards: number
  blockedCards: number
}

export interface TodayAgendaCard {
  boardId: string
  boardName: string
  cardId: string
  title: string
  dueDate: string | null
  blockReason: string | null
  updatedAt: string
}

export interface TodaySummary {
  workspaceMode: WorkspaceMode
  onboarding: WorkspaceOnboarding
  summary: TodayAgendaSummary
  overdueCards: TodayAgendaCard[]
  dueTodayCards: TodayAgendaCard[]
  blockedCards: TodayAgendaCard[]
  recommendedActions: HomeRecommendedAction[]
}
