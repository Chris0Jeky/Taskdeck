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

  it('marks notification as read', async () => {
    vi.mocked(http.post).mockResolvedValue({ data: { id: 'n1' } })

    await notificationsApi.markAsRead('n1')

    expect(http.post).toHaveBeenCalledWith('/notifications/n1/read')
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
