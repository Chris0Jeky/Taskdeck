import { describe, expect, it } from 'vitest'
import {
  DEMO_ONBOARDING,
  buildDemoBoardList,
  buildDemoBoardDetail,
  buildDemoHomeSummary,
  buildDemoTodaySummary,
  buildDemoCaptureItems,
} from '../../utils/demoData'

describe('demoData', () => {
  describe('DEMO_ONBOARDING', () => {
    it('has active visibility and three steps', () => {
      expect(DEMO_ONBOARDING.visibility).toBe('active')
      expect(DEMO_ONBOARDING.steps).toHaveLength(3)
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
