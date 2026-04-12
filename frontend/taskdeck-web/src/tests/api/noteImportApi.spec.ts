import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { noteImportApi } from '../../api/noteImportApi'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
  },
}))

describe('noteImportApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('importMarkdown', () => {
    it('posts markdown import request', async () => {
      vi.mocked(http.post).mockResolvedValue({
        data: {
          itemsCreated: 2,
          items: [
            {
              captureItemId: 'item-1',
              textExcerpt: 'Section one content',
              sourceType: 'markdown',
              sourceRef: 'md://notes.md#Section-One',
            },
            {
              captureItemId: 'item-2',
              textExcerpt: 'Section two content',
              sourceType: 'markdown',
              sourceRef: 'md://notes.md#Section-Two',
            },
          ],
        },
      })

      const result = await noteImportApi.importMarkdown({
        fileName: 'notes.md',
        content: '# Section One\nContent\n\n# Section Two\nMore content',
        boardId: 'board-123',
      })

      expect(http.post).toHaveBeenCalledWith('/import/notes/markdown', {
        fileName: 'notes.md',
        content: '# Section One\nContent\n\n# Section Two\nMore content',
        boardId: 'board-123',
      })
      expect(result.itemsCreated).toBe(2)
      expect(result.items).toHaveLength(2)
      expect(result.items[0].sourceType).toBe('markdown')
    })

    it('posts markdown import without boardId', async () => {
      vi.mocked(http.post).mockResolvedValue({
        data: { itemsCreated: 1, items: [] },
      })

      await noteImportApi.importMarkdown({
        fileName: 'notes.md',
        content: '# Hello',
      })

      expect(http.post).toHaveBeenCalledWith('/import/notes/markdown', {
        fileName: 'notes.md',
        content: '# Hello',
      })
    })
  })

  describe('importWebClip', () => {
    it('posts web clip import request', async () => {
      vi.mocked(http.post).mockResolvedValue({
        data: {
          itemsCreated: 1,
          items: [
            {
              captureItemId: 'clip-1',
              textExcerpt: '[Web Clip] https://example.com',
              sourceType: 'webclip',
              sourceRef: 'https://example.com',
            },
          ],
        },
      })

      const result = await noteImportApi.importWebClip({
        url: 'https://example.com',
        content: 'Important article content',
        title: 'Article Title',
        boardId: null,
      })

      expect(http.post).toHaveBeenCalledWith('/import/notes/webclip', {
        url: 'https://example.com',
        content: 'Important article content',
        title: 'Article Title',
        boardId: null,
      })
      expect(result.itemsCreated).toBe(1)
      expect(result.items[0].sourceType).toBe('webclip')
    })

    it('posts web clip without optional fields', async () => {
      vi.mocked(http.post).mockResolvedValue({
        data: { itemsCreated: 1, items: [] },
      })

      await noteImportApi.importWebClip({
        url: 'https://example.com',
        content: 'Clip content',
      })

      expect(http.post).toHaveBeenCalledWith('/import/notes/webclip', {
        url: 'https://example.com',
        content: 'Clip content',
      })
    })
  })
})
