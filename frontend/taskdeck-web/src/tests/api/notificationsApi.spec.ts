import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { notificationsApi } from '../../api/notificationsApi'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
  },
}))

describe('notificationsApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('queries notifications with filters', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: [] })

    await notificationsApi.getNotifications({ unreadOnly: true, limit: 25 })

    expect(http.get).toHaveBeenCalledWith('/notifications?unreadOnly=true&limit=25')
  })

  it('queries notifications with board scope and filters together', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: [] })

    await notificationsApi.getNotifications({ boardId: 'board-7', unreadOnly: true, limit: 25 })

    expect(http.get).toHaveBeenCalledWith('/notifications?boardId=board-7&unreadOnly=true&limit=25')
  })

  it('marks notification as read', async () => {
    vi.mocked(http.post).mockResolvedValue({ data: { id: 'n1' } })

    await notificationsApi.markAsRead('n1')

    expect(http.post).toHaveBeenCalledWith('/notifications/n1/read')
  })

  it('marks all notifications as read without board scope', async () => {
    vi.mocked(http.post).mockResolvedValue({ data: { markedCount: 5 } })

    const result = await notificationsApi.markAllRead()

    expect(http.post).toHaveBeenCalledWith('/notifications/mark-all-read')
    expect(result.markedCount).toBe(5)
  })

  it('marks all notifications as read with board scope', async () => {
    vi.mocked(http.post).mockResolvedValue({ data: { markedCount: 3 } })

    const result = await notificationsApi.markAllRead('board-42')

    expect(http.post).toHaveBeenCalledWith('/notifications/mark-all-read?boardId=board-42')
    expect(result.markedCount).toBe(3)
  })

  it('updates preferences', async () => {
    vi.mocked(http.put).mockResolvedValue({ data: { userId: 'u1' } })

    await notificationsApi.updatePreferences({
      inAppChannelEnabled: true,
      mentionImmediateEnabled: true,
      mentionDigestEnabled: false,
      assignmentImmediateEnabled: true,
      assignmentDigestEnabled: false,
      proposalOutcomeImmediateEnabled: true,
      proposalOutcomeDigestEnabled: false,
    })

    expect(http.put).toHaveBeenCalledWith('/notifications/preferences', expect.any(Object))
  })
})
