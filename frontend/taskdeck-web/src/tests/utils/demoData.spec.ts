import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  DEMO_ONBOARDING,
  DEMO_PROPOSAL_ID,
  buildDemoBoardList,
  buildDemoBoardDetail,
  buildDemoHomeSummary,
  buildDemoTodaySummary,
  buildDemoCaptureItems,
  buildDemoParticipants,
  buildDemoProposalPreview,
  buildDemoProposals,
  buildDemoCalendarData,
  buildDemoThinkingDeck,
} from '../../utils/demoData'
import { toCalendarDateKey } from '../../utils/dueDates'
import { installTimeZone } from './timeZone'

describe('demoData', () => {
  // `installTimeZone`, not `vi.stubEnv('TZ', …)`: the env stub only moves the
  // runtime zone under the default `forks` pool, and Stryker's Vitest dry run
  // forces `pool: 'threads'`, where these rows silently measured the host zone
  // instead (#2943).
  let restoreZone: (() => void) | null = null

  afterEach(() => {
    restoreZone?.()
    restoreZone = null
    vi.useRealTimers()
  })

  describe('DEMO_ONBOARDING', () => {
    it('has active visibility and the four capture-to-board loop steps', () => {
      expect(DEMO_ONBOARDING.visibility).toBe('active')
      expect(DEMO_ONBOARDING.steps).toHaveLength(4)
      expect(DEMO_ONBOARDING.steps.map((step) => step.stepId)).toContain('apply')
    })
  })

  describe('buildDemoBoardList', () => {
    it('returns two boards with required fields', () => {
      const boards = buildDemoBoardList()
      expect(boards).toHaveLength(2)
      for (const board of boards) {
        expect(board.id).toBeTruthy()
        expect(board.name).toBeTruthy()
        expect(board.isArchived).toBe(false)
        expect(board.createdAt).toBeTruthy()
      }
    })
  })

  describe('buildDemoBoardDetail', () => {
    it('returns a board with columns and cards', () => {
      const { board, cards } = buildDemoBoardDetail('demo-board-1')
      expect(board.id).toBe('demo-board-1')
      expect(board.columns.length).toBeGreaterThan(0)
      expect(cards.length).toBeGreaterThan(0)
      for (const card of cards) {
        expect(card.boardId).toBe('demo-board-1')
      }
    })

    it('falls back gracefully for unknown board ids', () => {
      const { board } = buildDemoBoardDetail('unknown-id')
      expect(board.id).toBe('unknown-id')
      expect(board.name).toBe('Demo Board')
    })
  })

  describe('buildDemoHomeSummary', () => {
    it('returns a complete home summary', () => {
      const summary = buildDemoHomeSummary()
      expect(summary.workspaceMode).toBe('guided')
      expect(summary.boards.recentBoards.length).toBeGreaterThan(0)
      expect(summary.recommendedActions.length).toBeGreaterThan(0)
      expect(summary.workload.capturesNeedingTriage).toBeGreaterThan(0)
      expect(summary.workload.proposalsPendingReview).toBe(1)
    })
  })

  describe('buildDemoTodaySummary', () => {
    it('uses relative dates for overdue cards', () => {
      const summary = buildDemoTodaySummary()
      expect(summary.overdueCards).toHaveLength(1)
      const overdueDate = new Date(summary.overdueCards[0].dueDate!)
      expect(overdueDate.getTime()).toBeLessThan(Date.now())
    })

    it('includes due-today cards', () => {
      const summary = buildDemoTodaySummary()
      expect(summary.dueTodayCards.length).toBeGreaterThan(0)
    })

    it('uses the same board card ids Home and the board editor share', () => {
      const summary = buildDemoTodaySummary()
      const { cards } = buildDemoBoardDetail('demo-board-1')
      expect(cards.some(card => card.id === summary.overdueCards[0]?.cardId)).toBe(true)
      expect(summary.overdueCards[0]?.cardId).toBe('demo-board-1-card-2')
    })

    it.each([
      ['America/Los_Angeles', '2026-08-24T00:30:00.000Z', '2026-08-23', '2026-08-22'],
      ['UTC', '2026-08-24T00:30:00.000Z', '2026-08-24', '2026-08-23'],
      ['Pacific/Kiritimati', '2026-08-24T12:30:00.000Z', '2026-08-25', '2026-08-24'],
    ])('uses the local calendar day for Today demo buckets in %s', (timeZone, instant, todayKey, yesterdayKey) => {
      vi.useFakeTimers()
      restoreZone = installTimeZone(timeZone)
      vi.setSystemTime(new Date(instant))

      const summary = buildDemoTodaySummary()

      expect(summary.dueTodayCards.length).toBeGreaterThan(0)
      expect(summary.dueTodayCards.every(card => toCalendarDateKey(card.dueDate) === todayKey)).toBe(true)
      expect(summary.dueTodayCards.every(card => card.dueDate === `${todayKey}T00:00:00.000Z`)).toBe(true)
      expect(summary.overdueCards).toHaveLength(1)
      expect(toCalendarDateKey(summary.overdueCards[0].dueDate)).toBe(yesterdayKey)
    })
  })

  describe('buildDemoCaptureItems', () => {
    it('returns capture items with valid status and source values', () => {
      const items = buildDemoCaptureItems()
      expect(items.length).toBeGreaterThan(0)
      for (const item of items) {
        expect(item.userId).toBeTruthy()
        expect(['New', 'Triaging', 'Triaged', 'ProposalCreated', 'Converted', 'Ignored', 'Failed']).toContain(item.status)
        expect(['Typed', 'Paste', 'TranscriptPaste', 'Import', 'Voice', 'MeetingIntegration']).toContain(item.source)
      }
    })
  })

  describe('buildDemoProposals', () => {
    it('returns one pending-review proposal matching Home', () => {
      const home = buildDemoHomeSummary()
      const proposals = buildDemoProposals()
      expect(proposals).toHaveLength(home.workload.proposalsPendingReview)
      expect(proposals[0]?.id).toBe(DEMO_PROPOSAL_ID)
      expect(proposals[0]?.status).toBe('PendingReview')
      expect(proposals[0]?.boardId).toBe('demo-board-1')
    })
  })

  describe('buildDemoProposalPreview', () => {
    it('uses the supplied in-memory proposal snapshot, not a fresh fixture', () => {
      const proposal = {
        ...buildDemoProposals()[0]!,
        status: 'Approved' as const,
        updatedAt: '2026-09-17T12:00:00.000Z',
        approvedRevisionId: 'demo-rev-approved',
      }
      const preview = buildDemoProposalPreview(proposal)
      expect(preview.proposalId).toBe(proposal.id)
      expect(preview.status).toBe('Approved')
      expect(preview.proposalUpdatedAt).toBe('2026-09-17T12:00:00.000Z')
      expect(preview.effectiveRevisionId).toBe('demo-rev-approved')
    })
  })

  describe('buildDemoCalendarData', () => {
    it('returns a CalendarData payload whose cards match Today due items', () => {
      const data = buildDemoCalendarData('2020-01-01T00:00:00.000Z', '2099-01-01T00:00:00.000Z')
      expect(Array.isArray(data.cards)).toBe(true)
      expect(data.totalCards).toBe(data.cards.length)
      expect(data.cards.some(card => card.cardId === 'demo-board-1-card-2' && card.isOverdue)).toBe(true)
    })
  })

  describe('buildDemoThinkingDeck', () => {
    it('returns a ThinkingDeck with a layers array', () => {
      const deck = buildDemoThinkingDeck('demo-board-1-card-3')
      expect(deck).toEqual({
        cardId: 'demo-board-1-card-3',
        revision: 1,
        schemaVersion: 1,
        canWrite: true,
        layers: [],
      })
    })
  })

  describe('buildDemoParticipants', () => {
    it('includes the demo user so the card editor can load assignees', () => {
      const people = buildDemoParticipants()
      expect(people.some(person => person.userId.startsWith('demo-user-'))).toBe(true)
      expect(people.length).toBeGreaterThan(1)
    })
  })
})
