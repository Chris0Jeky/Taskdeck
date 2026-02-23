export interface NotificationItem {
  id: string
  userId: string
  boardId: string | null
  type: number | string
  cadence: number | string
  title: string
  message: string
  sourceEntityType: string | null
  sourceEntityId: string | null
  isRead: boolean
  readAt: string | null
  createdAt: string
  updatedAt: string
}

export interface NotificationQuery {
  unreadOnly?: boolean
  boardId?: string
  limit?: number
}

export interface NotificationPreference {
  userId: string
  inAppChannelEnabled: boolean
  mentionImmediateEnabled: boolean
  mentionDigestEnabled: boolean
  assignmentImmediateEnabled: boolean
  assignmentDigestEnabled: boolean
  proposalOutcomeImmediateEnabled: boolean
  proposalOutcomeDigestEnabled: boolean
  createdAt: string
  updatedAt: string
}

export interface UpdateNotificationPreferenceRequest {
  inAppChannelEnabled: boolean
  mentionImmediateEnabled: boolean
  mentionDigestEnabled: boolean
  assignmentImmediateEnabled: boolean
  assignmentDigestEnabled: boolean
  proposalOutcomeImmediateEnabled: boolean
  proposalOutcomeDigestEnabled: boolean
}
