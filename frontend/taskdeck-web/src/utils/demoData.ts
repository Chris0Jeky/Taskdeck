import type { Board, BoardDetail, BoardParticipant, Card } from '../types/board'
import type { CaptureItemSummary } from '../types/capture'
import type { ChatMessage, ChatProviderHealth, ChatSession } from '../types/chat'
import type { Proposal, ProposalPreview } from '../types/automation'
import type { ThinkingDeck } from '../types/thinking'
import type {
  CalendarCard,
  CalendarData,
  HomeSummary,
  TodaySummary,
  WorkspaceOnboarding,
} from '../types/workspace'
import {
  addCalendarDays,
  calendarDateKeyToMidnightUtc,
  localCalendarDateKey,
  toCalendarDateKey,
} from './dueDates'
import { DEMO_TEAMMATE, DEMO_USER } from './demoIdentity'

export const DEMO_PROPOSAL_ID = 'demo-proposal-1'
export const DEMO_CHAT_SESSION_ID = 'demo-chat-session-1'

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
    { stepId: 'apply', title: 'Apply to a board', description: 'Apply an approved proposal so the change reaches your board.', targetSurface: 'board', isComplete: false },
  ],
}

const DEMO_BOARDS: Record<string, { name: string; description: string }> = {
  'demo-board-1': { name: 'Product Backlog', description: 'Feature requests and bug reports.' },
  'demo-board-2': { name: 'Sprint 12', description: 'Current sprint work items.' },
}

function demoDueDate(daysFromToday: number): string {
  const dateKey = addCalendarDays(localCalendarDateKey(), daysFromToday)
  const dueDate = dateKey ? calendarDateKeyToMidnightUtc(dateKey) : null
  if (!dueDate) throw new Error('Unable to build demo due date')
  return dueDate
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
    canWrite: true,
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
    canWrite: true,
    createdAt: ts,
    updatedAt: ts,
    columns: [
      { id: `${id}-col-1`, boardId: id, name: 'To Do', position: 0, wipLimit: null, cardCount: 2, createdAt: ts, updatedAt: ts },
      { id: `${id}-col-2`, boardId: id, name: 'In Progress', position: 1, wipLimit: 3, cardCount: 1, createdAt: ts, updatedAt: ts },
      { id: `${id}-col-3`, boardId: id, name: 'Done', position: 2, wipLimit: null, cardCount: 1, createdAt: ts, updatedAt: ts },
    ],
  }

  const card2Due = id === 'demo-board-1' ? demoDueDate(-1) : null
  const sprintDue = id === 'demo-board-2' ? demoDueDate(0) : null
  const cards: Card[] = [
    { id: `${id}-card-1`, boardId: id, columnId: `${id}-col-1`, title: 'Set up CI pipeline', description: 'Configure GitHub Actions for build and test.', dueDate: null, isBlocked: false, blockReason: null, position: 0, labels: [], workItemType: 'Task', createdAt: ts, updatedAt: ts },
    { id: `${id}-card-2`, boardId: id, columnId: `${id}-col-1`, title: 'Design landing page', description: 'Create mockups for the new landing page.', dueDate: card2Due, isBlocked: false, blockReason: null, position: 1, labels: [], workItemType: 'Task', parentCardId: `${id}-card-1`, createdAt: ts, updatedAt: ts },
    {
      id: `${id}-card-3`,
      boardId: id,
      columnId: `${id}-col-2`,
      title: 'Implement dark mode',
      description: 'Apply Obsidian & Ember tokens across all views.',
      dueDate: sprintDue,
      isBlocked: false,
      blockReason: null,
      position: 0,
      labels: [],
      workItemType: 'Task',
      assignments: [{ userId: DEMO_USER.id, displayName: 'Demo user', assignedAt: ts, assignedByUserId: DEMO_USER.id }],
      createdAt: ts,
      updatedAt: ts,
    },
    { id: `${id}-card-4`, boardId: id, columnId: `${id}-col-3`, title: 'Write README', description: 'Document setup and usage instructions.', dueDate: sprintDue, isBlocked: false, blockReason: null, position: 0, labels: [], workItemType: 'Task', createdAt: ts, updatedAt: ts },
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
      { boardId: 'demo-board-1', boardName: 'Product Backlog', cardId: 'demo-board-1-card-2', title: 'Design landing page', dueDate: demoDueDate(-1), blockReason: null, updatedAt: now() },
    ],
    dueTodayCards: [
      { boardId: 'demo-board-2', boardName: 'Sprint 12', cardId: 'demo-board-2-card-3', title: 'Implement dark mode', dueDate: demoDueDate(0), blockReason: null, updatedAt: now() },
      { boardId: 'demo-board-2', boardName: 'Sprint 12', cardId: 'demo-board-2-card-4', title: 'Write README', dueDate: demoDueDate(0), blockReason: null, updatedAt: now() },
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

export function buildDemoParticipants(): BoardParticipant[] {
  return [
    { userId: DEMO_USER.id, displayName: 'Demo user' },
    { userId: DEMO_TEAMMATE.id, displayName: DEMO_TEAMMATE.displayName },
  ]
}

export function buildDemoProposals(): Proposal[] {
  const ts = now()
  const expiresAt = new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString()
  return [
    {
      id: DEMO_PROPOSAL_ID,
      sourceType: 'Chat',
      sourceReferenceId: DEMO_CHAT_SESSION_ID,
      boardId: 'demo-board-1',
      requestedByUserId: DEMO_USER.id,
      status: 'PendingReview',
      riskLevel: 'Low',
      summary: 'Split dark mode into three follow-up cards on Product Backlog',
      diffPreview: '+ Create card "Token audit"\n+ Create card "Sidebar contrast"\n+ Create card "Settings preview"',
      validationIssues: null,
      createdAt: ts,
      updatedAt: ts,
      expiresAt,
      decidedAt: null,
      decidedByUserId: null,
      appliedAt: null,
      failureReason: null,
      correlationId: 'demo-corr-1',
      operations: [
        {
          id: 'demo-op-1',
          proposalId: DEMO_PROPOSAL_ID,
          sequence: 0,
          actionType: 'CreateCard',
          targetType: 'Card',
          targetId: null,
          parameters: '{"title":"Token audit"}',
          idempotencyKey: 'demo-k-1',
          expectedVersion: null,
        },
      ],
      presentation: {
        plainSummary: 'Split dark mode into three follow-up cards on Product Backlog',
        impactSummary: '1 operation · explicit review · atomic apply',
        riskCue: 'Low risk · confirm before apply',
        sourceCue: 'From chat',
        operationHeadlines: ['Create card "Token audit"'],
        affectedEntities: [{ entityType: 'Board', entityId: 'demo-board-1', label: 'Product Backlog', changeCount: 1 }],
      },
      isExpired: false,
      deferredUntil: null,
      approvedRevisionId: null,
      latestRevisionId: null,
    },
  ]
}

export function buildDemoProposalPreview(proposal: Proposal): ProposalPreview {
  const effectiveRevisionId = proposal.status === 'Approved'
    ? proposal.approvedRevisionId
    : proposal.latestRevisionId
  return {
    proposalId: proposal.id,
    boardId: proposal.boardId,
    status: proposal.status,
    effectiveRevisionId,
    effectiveRevisionNumber: null,
    proposalUpdatedAt: proposal.updatedAt,
    expiresAt: proposal.expiresAt,
    checkedAt: now(),
    diff: proposal.diffPreview ?? 'No diff in this demo proposal.',
  }
}

export function buildDemoCalendarData(from: string, to: string): CalendarData {
  const fromMs = Date.parse(from)
  const toMs = Date.parse(to)
  const todayKey = localCalendarDateKey()
  const cards: CalendarCard[] = []

  for (const board of buildDemoBoardList()) {
    const detail = buildDemoBoardDetail(board.id)
    for (const card of detail.cards) {
      if (!card.dueDate) continue
      const due = Date.parse(card.dueDate)
      if (Number.isFinite(fromMs) && due < fromMs) continue
      if (Number.isFinite(toMs) && due >= toMs) continue
      const column = detail.board.columns.find((item) => item.id === card.columnId)
      const dueKey = toCalendarDateKey(card.dueDate)
      cards.push({
        cardId: card.id,
        boardId: board.id,
        boardName: board.name,
        columnId: card.columnId,
        columnName: column?.name ?? 'To Do',
        title: card.title,
        dueDate: card.dueDate,
        isBlocked: card.isBlocked,
        blockReason: card.blockReason,
        isOverdue: Boolean(dueKey && dueKey < todayKey),
        updatedAt: card.updatedAt,
      })
    }
  }

  return {
    from: from || now(),
    to: to || now(),
    totalCards: cards.length,
    cards,
  }
}

export function buildDemoThinkingDeck(cardId: string): ThinkingDeck {
  return {
    cardId,
    revision: 1,
    schemaVersion: 1,
    canWrite: true,
    layers: [],
  }
}

export function buildDemoChatHealth(): ChatProviderHealth {
  return {
    isAvailable: true,
    providerName: 'Mock',
    errorMessage: null,
    model: 'demo-static',
    isMock: true,
    isProbed: true,
    verificationStatus: 'verified',
    probeLatencyMs: 1,
  }
}

export function buildDemoChatSessions(): ChatSession[] {
  const ts = now()
  const messages: ChatMessage[] = [
    {
      id: 'demo-chat-msg-1',
      sessionId: DEMO_CHAT_SESSION_ID,
      role: 'User',
      content: 'Split the dark-mode work into smaller cards.',
      messageType: 'text',
      proposalId: null,
      tokenUsage: null,
      createdAt: ts,
    },
    {
      id: 'demo-chat-msg-2',
      sessionId: DEMO_CHAT_SESSION_ID,
      role: 'Assistant',
      content: 'I drafted one proposal: split dark mode into three follow-up cards. Open Review to decide before anything reaches the board.',
      messageType: 'proposal-reference',
      proposalId: DEMO_PROPOSAL_ID,
      tokenUsage: 42,
      createdAt: ts,
    },
  ]
  return [
    {
      id: DEMO_CHAT_SESSION_ID,
      userId: DEMO_USER.id,
      boardId: 'demo-board-1',
      title: 'Dark mode follow-ups',
      status: 'Active',
      createdAt: ts,
      updatedAt: ts,
      recentMessages: messages,
    },
  ]
}
