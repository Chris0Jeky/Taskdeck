import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import NotificationPreferencesView from '../../views/NotificationPreferencesView.vue'

const mockNotificationStore = reactive({
  preferences: {
    userId: 'user-1',
    inAppChannelEnabled: true,
    mentionImmediateEnabled: true,
    mentionDigestEnabled: false,
    assignmentImmediateEnabled: true,
    assignmentDigestEnabled: false,
    proposalOutcomeImmediateEnabled: true,
    proposalOutcomeDigestEnabled: false,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
  },
  loading: false,
  error: null as string | null,
  fetchPreferences: vi.fn<() => Promise<void>>(),
  updatePreferences: vi.fn<(payload: Record<string, boolean>) => Promise<void>>(),
})

vi.mock('../../store/notificationStore', () => ({
  useNotificationStore: () => mockNotificationStore,
}))

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (_error: unknown, fallback: string) => ({ message: fallback }),
}))

async function waitForUi() {
  await Promise.resolve()
  await Promise.resolve()
}

describe('NotificationPreferencesView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockNotificationStore.fetchPreferences.mockResolvedValue(undefined)
    mockNotificationStore.updatePreferences.mockResolvedValue(undefined)
    mockNotificationStore.preferences = {
      userId: 'user-1',
      inAppChannelEnabled: true,
      mentionImmediateEnabled: true,
      mentionDigestEnabled: false,
      assignmentImmediateEnabled: true,
      assignmentDigestEnabled: false,
      proposalOutcomeImmediateEnabled: true,
      proposalOutcomeDigestEnabled: false,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    }
  })

  it('loads preferences on mount', async () => {
    mount(NotificationPreferencesView)
    await waitForUi()

    expect(mockNotificationStore.fetchPreferences).toHaveBeenCalledTimes(1)
  })

  it('submits updated preference values', async () => {
    const wrapper = mount(NotificationPreferencesView)
    await waitForUi()

    const toggles = wrapper.findAll('input[type="checkbox"]')
    await toggles[1].setValue(false)
    await toggles[2].setValue(true)

    await wrapper.get('form').trigger('submit.prevent')

    expect(mockNotificationStore.updatePreferences).toHaveBeenCalledWith(expect.objectContaining({
      mentionImmediateEnabled: false,
      mentionDigestEnabled: true,
    }))
  })
})
