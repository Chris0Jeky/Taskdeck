import { describe, it, expect, beforeEach, vi } from 'vitest'
import { cardCommentsApi } from '../../api/cardCommentsApi'
import http from '../../api/http'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    patch: vi.fn(),
    delete: vi.fn(),
  },
}))

describe('cardCommentsApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('should fetch card comments', async () => {
    const payload = [{ id: 'comment-1', content: 'Comment' }]
    vi.mocked(http.get).mockResolvedValue({ data: payload })

    const result = await cardCommentsApi.getComments('board-1', 'card-1')

    expect(http.get).toHaveBeenCalledWith('/boards/board-1/cards/card-1/comments')
    expect(result).toEqual(payload)
  })

  it('should create a card comment', async () => {
    const payload = { id: 'comment-1', content: 'Created comment' }
    vi.mocked(http.post).mockResolvedValue({ data: payload })

    const result = await cardCommentsApi.createComment('board-1', 'card-1', { content: 'Created comment' })

    expect(http.post).toHaveBeenCalledWith('/boards/board-1/cards/card-1/comments', {
      content: 'Created comment',
    })
    expect(result).toEqual(payload)
  })

  it('should update a card comment', async () => {
    const payload = { id: 'comment-1', content: 'Updated comment' }
    vi.mocked(http.patch).mockResolvedValue({ data: payload })

    const result = await cardCommentsApi.updateComment('board-1', 'card-1', 'comment-1', {
      content: 'Updated comment',
    })

    expect(http.patch).toHaveBeenCalledWith('/boards/board-1/cards/card-1/comments/comment-1', {
      content: 'Updated comment',
    })
    expect(result).toEqual(payload)
  })

  it('should delete a card comment', async () => {
    vi.mocked(http.delete).mockResolvedValue({})

    await cardCommentsApi.deleteComment('board-1', 'card-1', 'comment-1')

    expect(http.delete).toHaveBeenCalledWith('/boards/board-1/cards/card-1/comments/comment-1')
  })
})
