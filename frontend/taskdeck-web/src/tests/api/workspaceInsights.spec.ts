import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { workspaceInsightsApi } from '../../api/workspaceInsights'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    patch: vi.fn(),
  },
}))

describe('workspaceInsightsApi', () => {
  beforeEach(() => vi.clearAllMocks())

  it('reads board-scoped insights', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: [] })

    await workspaceInsightsApi.getInsights('board/1')

    expect(http.get).toHaveBeenCalledWith('/workspace-insights?boardId=board%2F1')
  })

  it('starts analysis with the selected board', async () => {
    vi.mocked(http.post).mockResolvedValue({ data: [] })

    await workspaceInsightsApi.analyzeBoard({ boardId: 'board-1' })

    expect(http.post).toHaveBeenCalledWith('/workspace-insights/analyze', { boardId: 'board-1' })
  })

  it('sends insight actions and exact answer evidence', async () => {
    vi.mocked(http.patch).mockResolvedValue({ data: {} })
    vi.mocked(http.post).mockResolvedValue({ data: {} })

    await workspaceInsightsApi.updateInsight('insight/1', 'dismiss')
    await workspaceInsightsApi.answerInsight('insight/1', {
      text: 'A private answer',
      status: 'needsReview',
      evidence: 'The card is blocked by copy review.',
    })

    expect(http.patch).toHaveBeenCalledWith('/workspace-insights/insight%2F1', { action: 'dismiss' })
    expect(http.post).toHaveBeenCalledWith('/workspace-insights/insight%2F1/answer', {
      text: 'A private answer',
      status: 'needsReview',
      evidence: 'The card is blocked by copy review.',
    })
  })

  it('reads and mutates revisioned board memory', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: [] })
    vi.mocked(http.post).mockResolvedValue({ data: {} })
    vi.mocked(http.put).mockResolvedValue({ data: {} })
    vi.mocked(http.patch).mockResolvedValue({ data: {} })

    await workspaceInsightsApi.getMemories('board-1', false)
    await workspaceInsightsApi.createMemory({ boardId: 'board-1', title: 'T', text: 'X', status: 'statement' })
    await workspaceInsightsApi.updateMemory('memory/1', {
      title: 'T2',
      text: 'X2',
      status: 'assumption',
      revision: 3,
    })
    await workspaceInsightsApi.setMemoryArchived('memory/1', { archived: true, revision: 4 })

    expect(http.get).toHaveBeenCalledWith('/workspace-memory?boardId=board-1&archived=false')
    expect(http.post).toHaveBeenCalledWith('/workspace-memory', {
      boardId: 'board-1',
      title: 'T',
      text: 'X',
      status: 'statement',
    })
    expect(http.put).toHaveBeenCalledWith('/workspace-memory/memory%2F1', {
      title: 'T2',
      text: 'X2',
      status: 'assumption',
      revision: 3,
    })
    expect(http.patch).toHaveBeenCalledWith('/workspace-memory/memory%2F1', {
      archived: true,
      revision: 4,
    })
  })
})
