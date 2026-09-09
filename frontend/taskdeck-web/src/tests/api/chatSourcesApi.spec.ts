import { beforeEach, expect, it, vi } from 'vitest'
import { chatSourcesApi } from '../../api/chatSourcesApi'
const http = vi.hoisted(() => ({ get: vi.fn() }))
vi.mock('../../api/http', () => ({ default: http }))
beforeEach(() => vi.clearAllMocks())
it('uses an encoded source identity and explicit board, version and page', async () => {
  const page = { memoryId: 'memory', revision: 3, items: [], nextOffset: null }
  http.get.mockResolvedValue({ data: page })
  expect(await chatSourcesApi.list('memory/id', 'board', 3, 10)).toEqual(page)
  expect(http.get).toHaveBeenCalledWith('/llm/chat/context-memory/memory%2Fid/sources', { params: { boardId: 'board', revision: 3, offset: 10 } })
})
it('preserves a failed authorization or stale version for the picker retry state', async () => {
  const failure = new Error('stale'); http.get.mockRejectedValue(failure)
  await expect(chatSourcesApi.list('memory', 'board', 2)).rejects.toBe(failure)
})
