import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  DEMO_ONBOARDING,
  buildDemoBoardList,
  buildDemoBoardDetail,
  buildDemoHomeSummary,
  buildDemoTodaySummary,
  buildDemoCaptureItems,
} from '../../utils/demoData'
import { toCalendarDateKey } from '../../utils/dueDates'

describe('demoData', () => {
  afterEach(() => {
    vi.unstubAllEnvs()
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

    it.each([
      ['America/Los_Angeles', '2026-08-24T00:30:00.000Z', '2026-08-23', '2026-08-22'],
      ['UTC', '2026-08-24T00:30:00.000Z', '2026-08-24', '2026-08-23'],
      ['Pacific/Kiritimati', '2026-08-24T12:30:00.000Z', '2026-08-25', '2026-08-24'],
    ])('uses the local calendar day for Today demo buckets in %s', (timeZone, instant, todayKey, yesterdayKey) => {
      vi.stubEnv('TZ', timeZone)
      vi.useFakeTimers()
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
})
