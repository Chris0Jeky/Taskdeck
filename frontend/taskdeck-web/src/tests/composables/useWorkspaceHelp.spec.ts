import { beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick, reactive } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import { useWorkspaceHelp } from '../../composables/useWorkspaceHelp'
import { WORKSPACE_HELP_DISMISSALS_STORAGE_KEY } from '../../utils/storageKeys'

const sessionStore = reactive({
  userId: 'user-1' as string | null,
})

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: () => sessionStore,
}))

describe('useWorkspaceHelp', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
    sessionStore.userId = 'user-1'
  })

  it('persists dismiss and replay state per help topic and current user', () => {
    const help = useWorkspaceHelp('review')

    expect(help.isVisible.value).toBe(true)

    help.dismiss()

    expect(help.isDismissed.value).toBe(true)
    expect(help.isVisible.value).toBe(false)
    expect(localStorage.getItem(`${WORKSPACE_HELP_DISMISSALS_STORAGE_KEY}:user-1`)).toBe(JSON.stringify({ review: true }))

    const replayedHelp = useWorkspaceHelp('review')
    expect(replayedHelp.isVisible.value).toBe(false)

    replayedHelp.replay()

    expect(replayedHelp.isVisible.value).toBe(true)
    expect(localStorage.getItem(`${WORKSPACE_HELP_DISMISSALS_STORAGE_KEY}:user-1`)).toBeNull()
  })

  it('ignores malformed persisted state', () => {
    localStorage.setItem(`${WORKSPACE_HELP_DISMISSALS_STORAGE_KEY}:user-1`, '{not-json')

    const help = useWorkspaceHelp('home')

    expect(help.isVisible.value).toBe(true)

    help.dismiss()

    expect(localStorage.getItem(`${WORKSPACE_HELP_DISMISSALS_STORAGE_KEY}:user-1`)).toBe(JSON.stringify({ home: true }))
  })

  it('keeps dismissal state scoped when the current user changes', async () => {
    const help = useWorkspaceHelp('activity-selectors')

    help.dismiss()
    expect(help.isVisible.value).toBe(false)

    sessionStore.userId = 'user-2'
    await nextTick()

    expect(help.isVisible.value).toBe(true)
    expect(localStorage.getItem(`${WORKSPACE_HELP_DISMISSALS_STORAGE_KEY}:user-1`)).toBe(JSON.stringify({ 'activity-selectors': true }))
    expect(localStorage.getItem(`${WORKSPACE_HELP_DISMISSALS_STORAGE_KEY}:user-2`)).toBeNull()
  })

  it('tracks selector guidance independently per surface', () => {
    const activityHelp = useWorkspaceHelp('activity-selectors')

    activityHelp.dismiss()

    const accessHelp = useWorkspaceHelp('board-access-selectors')

    expect(activityHelp.isVisible.value).toBe(false)
    expect(accessHelp.isVisible.value).toBe(true)
    expect(localStorage.getItem(`${WORKSPACE_HELP_DISMISSALS_STORAGE_KEY}:user-1`))
      .toBe(JSON.stringify({ 'activity-selectors': true }))
  })

  it('migrates legacy dismissal storage into the current user scope', () => {
    localStorage.setItem(WORKSPACE_HELP_DISMISSALS_STORAGE_KEY, JSON.stringify({ today: true }))

    const help = useWorkspaceHelp('today')

    expect(help.isVisible.value).toBe(false)
    expect(localStorage.getItem(WORKSPACE_HELP_DISMISSALS_STORAGE_KEY)).toBeNull()
    expect(localStorage.getItem(`${WORKSPACE_HELP_DISMISSALS_STORAGE_KEY}:user-1`)).toBe(JSON.stringify({ today: true }))
  })
})
