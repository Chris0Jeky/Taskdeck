import { beforeEach, describe, expect, it } from 'vitest'
import { useWorkspaceHelp } from '../../composables/useWorkspaceHelp'
import { WORKSPACE_HELP_DISMISSALS_STORAGE_KEY } from '../../utils/storageKeys'

describe('useWorkspaceHelp', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it('persists dismiss and replay state per help topic', () => {
    const help = useWorkspaceHelp('review')

    expect(help.isVisible.value).toBe(true)

    help.dismiss()

    expect(help.isDismissed.value).toBe(true)
    expect(help.isVisible.value).toBe(false)
    expect(localStorage.getItem(WORKSPACE_HELP_DISMISSALS_STORAGE_KEY)).toBe(JSON.stringify({ review: true }))

    const replayedHelp = useWorkspaceHelp('review')
    expect(replayedHelp.isVisible.value).toBe(false)

    replayedHelp.replay()

    expect(replayedHelp.isVisible.value).toBe(true)
    expect(localStorage.getItem(WORKSPACE_HELP_DISMISSALS_STORAGE_KEY)).toBeNull()
  })

  it('ignores malformed persisted state', () => {
    localStorage.setItem(WORKSPACE_HELP_DISMISSALS_STORAGE_KEY, '{not-json')

    const help = useWorkspaceHelp('home')

    expect(help.isVisible.value).toBe(true)

    help.dismiss()

    expect(localStorage.getItem(WORKSPACE_HELP_DISMISSALS_STORAGE_KEY)).toBe(JSON.stringify({ home: true }))
  })
})
