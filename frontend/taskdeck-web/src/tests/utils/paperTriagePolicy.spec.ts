import { describe, expect, it } from 'vitest'
import {
  canEditCapture,
  canSetDisposition,
  canTriageCapture,
  captureLabel,
  isBoardWritable,
  isPastEditing,
  isTriagedWithoutProposal,
} from '../../utils/paperTriagePolicy'
import type { Board } from '../../types/board'
import type { CaptureItemSummary, CaptureStatusValue } from '../../types/capture'

function makeItem(overrides: Partial<CaptureItemSummary> = {}): CaptureItemSummary {
  return {
    id: 'capture-1',
    userId: 'user-1',
    boardId: 'board-1',
    status: 'New',
    source: 'Typed',
    textExcerpt: 'An excerpt',
    createdAt: '2026-09-10T12:00:00.000Z',
    processedAt: null,
    ...overrides,
  }
}

function makeBoard(overrides: Partial<Board> = {}): Board {
  return {
    id: 'board-1',
    name: 'Board one',
    description: null,
    isArchived: false,
    createdAt: '2026-09-10T12:00:00.000Z',
    updatedAt: '2026-09-10T12:00:00.000Z',
    ...overrides,
  }
}

describe('paperTriagePolicy', () => {
  it('uses the visible excerpt as the correction label and falls back to the id', () => {
    expect(captureLabel(makeItem({ textExcerpt: '  A useful excerpt  ' }))).toBe('A useful excerpt')
    expect(captureLabel(makeItem({ textExcerpt: '   ' }))).toBe('capture-1')
  })

  it('recognizes only settled server-side edit refusals', () => {
    const settled = [3, 'ProposalCreated', 4, 'Converted', 5, 'Ignored'] as CaptureStatusValue[]
    const active = [0, 'New', 6, 'Failed', 2, 'Triaged', 'Triaging'] as CaptureStatusValue[]

    expect(settled.every(isPastEditing)).toBe(true)
    expect(active.some(isPastEditing)).toBe(false)
  })

  it('treats only proposal-less triaged captures as the special triage state', () => {
    expect(isTriagedWithoutProposal(makeItem({ status: 2 }))).toBe(true)
    expect(isTriagedWithoutProposal(makeItem({ status: 'Triaged' }))).toBe(true)
    expect(isTriagedWithoutProposal(makeItem({ status: 'New' }))).toBe(false)
    expect(isTriagedWithoutProposal(makeItem({ status: 'Triaging' }))).toBe(false)
  })

  it('keeps board write capability fail-open for legacy payloads only', () => {
    expect(isBoardWritable(makeBoard({ canWrite: true }))).toBe(true)
    expect(isBoardWritable(makeBoard({ canWrite: false }))).toBe(false)
    expect(isBoardWritable(makeBoard())).toBe(true)
  })

  it('applies the edit exception only when the server explicitly allows it', () => {
    expect(canEditCapture(makeItem({ status: 'New', canEditSuggestion: false }))).toBe(false)
    expect(canEditCapture(makeItem({ status: 'New' }))).toBe(true)
    expect(canEditCapture(makeItem({ status: 'Triaged', canEditSuggestion: true }))).toBe(true)
    expect(canEditCapture(makeItem({ status: 'Triaged' }))).toBe(false)
  })

  it('keeps triage available for proposal-less triage but not disposition actions', () => {
    expect(canTriageCapture(makeItem({ status: 'Triaged' }))).toBe(true)
    expect(canSetDisposition(makeItem({ status: 'Triaged' }))).toBe(false)
    expect(canTriageCapture(makeItem({ status: 'Failed' }))).toBe(true)
    expect(canSetDisposition(makeItem({ status: 'Failed' }))).toBe(true)
    expect(canTriageCapture(makeItem({ status: 'Converted' }))).toBe(false)
  })
})
