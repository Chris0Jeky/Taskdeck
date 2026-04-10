/**
 * archiveApi integration tests — verifies the archive API module boundary.
 *
 * No archiveStore exists; the archiveApi is consumed directly by ArchiveView.
 * These tests exercise the archiveApi → http chain including error handling,
 * query parameter construction, and response shape validation.
 */
import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { archiveApi } from '../../api/archiveApi'
import type { ArchiveItem, RestoreArchiveResult } from '../../types/archive'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    patch: vi.fn(),
    delete: vi.fn(),
  },
}))

function makeArchiveItem(overrides: Partial<ArchiveItem> = {}): ArchiveItem {
  return {
    id: 'arch-1',
    entityType: 'card',
    entityId: 'card-1',
    boardId: 'board-1',
    name: 'Archived Card',
    archivedByUserId: 'user-1',
    archivedAt: '2026-01-01T00:00:00Z',
    reason: null,
    restoreStatus: 'Available',
    restoredAt: null,
    restoredByUserId: null,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  }
}

describe('archiveApi — integration (mocked HTTP)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  // ── getItems ──────────────────────────────────────────────────────────────

  describe('getItems', () => {
    it('calls GET /archive/items and returns the response array', async () => {
      const items = [makeArchiveItem(), makeArchiveItem({ id: 'arch-2', name: 'Second' })]
      vi.mocked(http.get).mockResolvedValue({ data: items })

      const result = await archiveApi.getItems()

      expect(result).toHaveLength(2)
      expect(result[0].id).toBe('arch-1')
      expect(result[1].id).toBe('arch-2')
      expect(http.get).toHaveBeenCalledWith('/archive/items')
    })

    it('appends entityType filter to the query string', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: [] })

      await archiveApi.getItems({ entityType: 'card' })

      const calledUrl = vi.mocked(http.get).mock.calls[0][0] as string
      expect(calledUrl).toContain('entityType=card')
    })

    it('appends boardId filter to the query string', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: [] })

      await archiveApi.getItems({ boardId: 'board-xyz' })

      const calledUrl = vi.mocked(http.get).mock.calls[0][0] as string
      expect(calledUrl).toContain('boardId=board-xyz')
    })

    it('appends status filter to the query string', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: [] })

      await archiveApi.getItems({ status: 'Available' })

      const calledUrl = vi.mocked(http.get).mock.calls[0][0] as string
      expect(calledUrl).toContain('status=Available')
    })

    it('combines multiple filters in the query string', async () => {
      vi.mocked(http.get).mockResolvedValue({ data: [] })

      await archiveApi.getItems({ entityType: 'card', boardId: 'board-1', limit: 50 })

      const calledUrl = vi.mocked(http.get).mock.calls[0][0] as string
      expect(calledUrl).toContain('entityType=card')
      expect(calledUrl).toContain('boardId=board-1')
      expect(calledUrl).toContain('limit=50')
    })

    it('propagates errors from the HTTP layer', async () => {
      vi.mocked(http.get).mockRejectedValue(new Error('Network Error'))

      await expect(archiveApi.getItems()).rejects.toThrow('Network Error')
    })
  })

  // ── restoreItem ───────────────────────────────────────────────────────────

  describe('restoreItem', () => {
    it('posts to /archive/:entityType/:entityId/restore and returns the result', async () => {
      const result: RestoreArchiveResult = {
        success: true,
        restoredEntityId: 'card-restored',
        errorMessage: null,
        resolvedName: 'My Card',
      }
      vi.mocked(http.post).mockResolvedValue({ data: result })

      const response = await archiveApi.restoreItem('card', 'card-1', {
        targetBoardId: 'board-1',
        restoreMode: 0,
        conflictStrategy: 0,
      })

      expect(response.success).toBe(true)
      expect(response.restoredEntityId).toBe('card-restored')
      expect(http.post).toHaveBeenCalledWith(
        '/archive/card/card-1/restore',
        expect.objectContaining({ targetBoardId: 'board-1' }),
      )
    })

    it('URL-encodes special characters in entityType and entityId', async () => {
      vi.mocked(http.post).mockResolvedValue({
        data: { success: true, restoredEntityId: null, errorMessage: null, resolvedName: null },
      })

      await archiveApi.restoreItem('card/type', 'id+special', {
        targetBoardId: null,
        restoreMode: 0,
        conflictStrategy: 0,
      })

      const calledUrl = vi.mocked(http.post).mock.calls[0][0] as string
      expect(calledUrl).toContain('card%2Ftype')
      expect(calledUrl).toContain('id%2Bspecial')
    })

    it('returns failure result when the backend rejects the restore', async () => {
      const failResult: RestoreArchiveResult = {
        success: false,
        restoredEntityId: null,
        errorMessage: 'Board no longer exists',
        resolvedName: null,
      }
      vi.mocked(http.post).mockResolvedValue({ data: failResult })

      const response = await archiveApi.restoreItem('card', 'card-1', {
        targetBoardId: 'deleted-board',
        restoreMode: 0,
        conflictStrategy: 0,
      })

      expect(response.success).toBe(false)
      expect(response.errorMessage).toBe('Board no longer exists')
    })

    it('propagates HTTP errors from the restore endpoint', async () => {
      vi.mocked(http.post).mockRejectedValue({
        response: { status: 404, data: { message: 'Archive item not found' } },
      })

      await expect(
        archiveApi.restoreItem('card', 'missing', {
          targetBoardId: null,
          restoreMode: 0,
          conflictStrategy: 0,
        }),
      ).rejects.toBeDefined()
    })

    it('propagates 409 Conflict when restoring with name collision', async () => {
      vi.mocked(http.post).mockRejectedValue({
        response: { status: 409, data: { message: 'Name conflict' } },
      })

      await expect(
        archiveApi.restoreItem('card', 'card-1', {
          targetBoardId: 'board-1',
          restoreMode: 0,
          conflictStrategy: 0,
        }),
      ).rejects.toBeDefined()
    })
  })
})
