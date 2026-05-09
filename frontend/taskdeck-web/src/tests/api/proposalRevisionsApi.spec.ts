import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { proposalRevisionsApi } from '../../api/proposalRevisionsApi'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
  },
}))

describe('proposalRevisionsApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('creates a revision with payload and reason', async () => {
    const mockRevision = {
      id: 'rev-1',
      proposalId: 'p1',
      revisionNumber: 1,
      editorUserId: 'u1',
      revisedPayload: '{"title":"Edited"}',
      revisedAt: '2026-01-01T00:00:00Z',
      reason: 'Fix title',
      createdAt: '2026-01-01T00:00:00Z',
    }
    vi.mocked(http.post).mockResolvedValue({ data: mockRevision })

    const result = await proposalRevisionsApi.createRevision('p1', {
      revisedPayload: '{"title":"Edited"}',
      reason: 'Fix title',
    })

    expect(http.post).toHaveBeenCalledWith(
      '/automation/proposals/p1/revisions',
      { revisedPayload: '{"title":"Edited"}', reason: 'Fix title' },
    )
    expect(result).toEqual(mockRevision)
  })

  it('lists revisions for a proposal', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: [] })

    const result = await proposalRevisionsApi.getRevisions('p1')

    expect(http.get).toHaveBeenCalledWith('/automation/proposals/p1/revisions')
    expect(result).toEqual([])
  })

  it('returns latest revision when one exists', async () => {
    const mockRevision = {
      id: 'rev-2',
      proposalId: 'p1',
      revisionNumber: 2,
      editorUserId: 'u1',
      revisedPayload: '{"v":2}',
      revisedAt: '2026-01-01T00:00:00Z',
      reason: 'second',
      createdAt: '2026-01-01T00:00:00Z',
    }
    vi.mocked(http.get).mockResolvedValue({ data: mockRevision })

    const result = await proposalRevisionsApi.getLatestRevision('p1')

    expect(http.get).toHaveBeenCalledWith('/automation/proposals/p1/revisions/latest')
    expect(result).toEqual(mockRevision)
  })

  it('returns null when no latest revision (404)', async () => {
    vi.mocked(http.get).mockRejectedValue({ response: { status: 404 } })

    const result = await proposalRevisionsApi.getLatestRevision('p1')

    expect(result).toBeNull()
  })

  it('rethrows non-404 errors from getLatestRevision', async () => {
    const error = { response: { status: 500 } }
    vi.mocked(http.get).mockRejectedValue(error)

    await expect(proposalRevisionsApi.getLatestRevision('p1')).rejects.toEqual(error)
  })
})
