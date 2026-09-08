import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { collectWorkspaceMemoryExport, downloadWorkspaceMemoryJson } from '../../utils/workspaceMemoryExport'
import { workspaceInsightsApi } from '../../api/workspaceInsights'
import type { Memory } from '../../types/workspaceInsights'

vi.mock('../../api/workspaceInsights', () => ({ workspaceInsightsApi: { getMemories: vi.fn() } }))
const record: Memory = {
  id: 'memory-1', boardId: 'board-1', title: 'Original context', text: 'Correction',
  originalText: '  Original answer\nwith spacing  ', originalEvidence: 'Blocked by a device; card revision at creation',
  status: 'statement', archived: false, revision: 2, createdAt: '2026-09-08T00:00:00Z',
  history: [{ title: 'Original context', text: '  Original answer\nwith spacing  ', status: 'unknown', revision: 1, recordedAt: '2026-09-08T00:00:00Z' }],
}

describe('private workspace memory export', () => {
  beforeEach(() => vi.clearAllMocks())
  afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals() })
  it('preserves active and archived originals, evidence and history without modifying them', async () => {
    const archived = { ...record, id: 'memory-2', archived: true }
    vi.mocked(workspaceInsightsApi.getMemories).mockResolvedValueOnce([record]).mockResolvedValueOnce([archived])
    const json = await collectWorkspaceMemoryExport('board-1', () => true)
    expect(workspaceInsightsApi.getMemories).toHaveBeenNthCalledWith(1, 'board-1', false)
    expect(workspaceInsightsApi.getMemories).toHaveBeenNthCalledWith(2, 'board-1', true)
    expect(JSON.parse(json).memories).toEqual([record, archived])
    expect(record.originalText).toBe('  Original answer\nwith spacing  ')
    expect(JSON.parse(json).notice).toContain('not an atomic snapshot')
  })
  it('does not return a partial file when the archived request fails', async () => {
    vi.mocked(workspaceInsightsApi.getMemories).mockResolvedValueOnce([record]).mockRejectedValueOnce(new Error('Unavailable'))
    await expect(collectWorkspaceMemoryExport('board-1', () => true)).rejects.toThrow('Unavailable')
  })
  it('rejects a session switch and mismatched board data', async () => {
    vi.mocked(workspaceInsightsApi.getMemories).mockResolvedValueOnce([record]).mockResolvedValueOnce([])
    const current = vi.fn().mockReturnValueOnce(true).mockReturnValueOnce(false)
    await expect(collectWorkspaceMemoryExport('board-1', current)).rejects.toThrow('session changed')
    vi.mocked(workspaceInsightsApi.getMemories).mockResolvedValueOnce([{ ...record, boardId: 'other' }]).mockResolvedValueOnce([])
    await expect(collectWorkspaceMemoryExport('board-1', () => true)).rejects.toThrow('did not match')
  })
  it('keeps the latest revision once when a record moves between read sets', async () => {
    vi.mocked(workspaceInsightsApi.getMemories).mockResolvedValueOnce([record]).mockResolvedValueOnce([{ ...record, archived: true, revision: 3 }])
    const result = JSON.parse(await collectWorkspaceMemoryExport('board-1', () => true))
    expect(result.memories).toHaveLength(1)
    expect(result.memories[0]).toMatchObject({ archived: true, revision: 3, originalText: record.originalText })
  })
  it('cleans up browser resources when starting the download fails', () => {
    const revoke = vi.fn()
    vi.stubGlobal('URL', { createObjectURL: vi.fn(() => 'blob:memory-export'), revokeObjectURL: revoke })
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => { throw new Error('Download blocked') })
    expect(() => downloadWorkspaceMemoryJson('{}', 'board-1')).toThrow('Download blocked')
    expect(revoke).toHaveBeenCalledWith('blob:memory-export')
    expect(document.querySelector('a[download]')).toBeNull()
  })
})
