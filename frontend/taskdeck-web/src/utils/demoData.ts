import type { Board, BoardDetail, Card } from '../types/board'
import type { CaptureItemSummary } from '../types/capture'
import type {
  HomeSummary,
  TodaySummary,
  WorkspaceOnboarding,
} from '../types/workspace'
import { DEMO_USER } from './demoMode'

export const DEMO_ONBOARDING: WorkspaceOnboarding = {
  visibility: 'active',
  isComplete: false,
  currentStepId: 'capture',
  dismissedAt: null,
  completedAt: null,
  steps: [
    { stepId: 'board', title: 'Create a board', description: 'Set up a board to organise your work.', targetSurface: 'boards', isComplete: true },
    { stepId: 'capture', title: 'Capture a note', description: 'Drop a quick thought into the inbox.', targetSurface: 'capture', isComplete: false },
    { stepId: 'review', title: 'Review a proposal', description: 'Approve or reject a proposed change before it reaches a board.', targetSurface: 'review', isComplete: false },
  ],
}

const DEMO_BOARDS: Record<string, { name: string; description: string }> = {
  'demo-board-1': { name: 'Product Backlog', description: 'Feature requests and bug reports.' },
  'demo-board-2': { name: 'Sprint 12', description: 'Current sprint work items.' },
}

function yesterday(): string {
  return new Date(Date.now() - 86_400_000).toISOString()
}

function now(): string {
  return new Date().toISOString()
}

export function buildDemoBoardList(): Board[] {
  const ts = now()
  return Object.entries(DEMO_BOARDS).map(([id, b]) => ({
    id,
    name: b.name,
    description: b.description,
    isArchived: false,
    createdAt: ts,
    updatedAt: ts,
  }))
}

export function buildDemoBoardDetail(id: string): { board: BoardDetail; cards: Card[] } {
  const ts = now()
  const match = DEMO_BOARDS[id] ?? { name: 'Demo Board', description: 'A demo board.' }

  const board: BoardDetail = {
    id,
    name: match.name,
    description: match.description,
    isArchived: false,
    createdAt: ts,
    updatedAt: ts,
    columns: [
      { id: `${id}-col-1`, boardId: id, name: 'To Do', position: 0, wipLimit: null, cardCount: 2, createdAt: ts, updatedAt: ts },
      { id: `${id}-col-2`, boardId: id, name: 'In Progress', position: 1, wipLimit: 3, cardCount: 1, createdAt: ts, updatedAt: ts },
      { id: `${id}-col-3`, boardId: id, name: 'Done', position: 2, wipLimit: null, cardCount: 1, createdAt: ts, updatedAt: ts },
    ],
  }

  const cards: Card[] = [
    { id: `${id}-card-1`, boardId: id, columnId: `${id}-col-1`, title: 'Set up CI pipeline', description: 'Configure GitHub Actions for build and test.', dueDate: null, isBlocked: false, blockReason: null, position: 0, labels: [], createdAt: ts, updatedAt: ts },
    { id: `${id}-card-2`, boardId: id, columnId: `${id}-col-1`, title: 'Design landing page', description: 'Create mockups for the new landing page.', dueDate: '2026-03-30T00:00:00Z', isBlocked: false, blockReason: null, position: 1, labels: [], createdAt: ts, updatedAt: ts },
    { id: `${id}-card-3`, boardId: id, columnId: `${id}-col-2`, title: 'Implement dark mode', description: 'Apply Obsidian & Ember tokens across all views.', dueDate: null, isBlocked: false, blockReason: null, position: 0, labels: [], createdAt: ts, updatedAt: ts },
    { id: `${id}-card-4`, boardId: id, columnId: `${id}-col-3`, title: 'Write README', description: 'Document setup and usage instructions.', dueDate: null, isBlocked: false, blockReason: null, position: 0, labels: [], createdAt: ts, updatedAt: ts },
  ]

  return { board, cards }
}

export function buildDemoHomeSummary(): HomeSummary {
  return {
    workspaceMode: 'guided',
    isFirstRun: false,
    onboarding: DEMO_ONBOARDING,
    workload: { capturesNeedingTriage: 3, capturesInProgress: 1, capturesReadyForFollowUp: 2, proposalsPendingReview: 1 },
    boards: {
      totalBoards: 2,
      recentBoardsCount: 2,
      recentBoards: [
        { id: 'demo-board-1', name: 'Product Backlog', description: 'Feature requests and bug reports.', updatedAt: now() },
        { id: 'demo-board-2', name: 'Sprint 12', description: 'Current sprint work items.', updatedAt: now() },
      ],
    },
    recommendedActions: [
      { actionId: 'review-proposals', title: 'Review proposals', description: 'One proposal is waiting for your decision.', targetSurface: 'review', attentionCount: 1 },
      { actionId: 'triage-captures', title: 'Triage inbox', description: 'Three captures need sorting.', targetSurface: 'capture', attentionCount: 3 },
    ],
  }
}

export function buildDemoTodaySummary(): TodaySummary {
  return {
    workspaceMode: 'guided',
    onboarding: DEMO_ONBOARDING,
    summary: { capturesNeedingTriage: 3, proposalsPendingReview: 1, overdueCards: 1, dueTodayCards: 2, blockedCards: 0 },
    overdueCards: [
      { boardId: 'demo-board-1', boardName: 'Product Backlog', cardId: 'demo-card-1', title: 'Fix login redirect loop', dueDate: yesterday(), blockReason: null, updatedAt: now() },
    ],
    dueTodayCards: [
      { boardId: 'demo-board-2', boardName: 'Sprint 12', cardId: 'demo-card-2', title: 'Add dark-mode toggle', dueDate: now(), blockReason: null, updatedAt: now() },
      { boardId: 'demo-board-2', boardName: 'Sprint 12', cardId: 'demo-card-3', title: 'Write onboarding copy', dueDate: now(), blockReason: null, updatedAt: now() },
    ],
    blockedCards: [],
    recommendedActions: [
      { actionId: 'review-proposals', title: 'Review proposals', description: 'One proposal is waiting for your decision.', targetSurface: 'review', attentionCount: 1 },
    ],
  }
}

export function buildDemoCaptureItems(): CaptureItemSummary[] {
  const ts = now()
  return [
    { id: 'demo-cap-1', userId: DEMO_USER.id, boardId: null, status: 'New', source: 'Typed', textExcerpt: 'Investigate slow dashboard load times on large boards', createdAt: ts, processedAt: null },
    { id: 'demo-cap-2', userId: DEMO_USER.id, boardId: 'demo-board-1', status: 'Triaging', source: 'Typed', textExcerpt: 'Add keyboard shortcuts for card navigation', createdAt: ts, processedAt: null },
    { id: 'demo-cap-3', userId: DEMO_USER.id, boardId: null, status: 'New', source: 'Paste', textExcerpt: 'Consider adding a calendar view for due dates', createdAt: ts, processedAt: null },
  ]
}
