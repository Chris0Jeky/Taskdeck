import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const firstToken = 'eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJmaXJzdCJ9.synthetic'
const secondToken = 'eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJzZWNvbmQifQ.synthetic'
const readKeys = ['taskdeck_session_break', 'taskdeck_token', 'taskdeck_session'] as const

// The real storage module reads independently atomic localStorage keys. This
// storage seam injects another tab's write after one read has returned its old
// value, without relying on scheduling luck or a real second browser process.
function controlledStorage() {
  const values = new Map<string, string>()
  let afterRead: ((key: string) => void) | undefined
  const storage: Storage = {
    get length() { return values.size },
    clear: () => values.clear(),
    key: index => [...values.keys()][index] ?? null,
    getItem: key => {
      const value = values.get(key) ?? null
      afterRead?.(key)
      return value
    },
    removeItem: key => { values.delete(key) },
    setItem: (key, value) => { values.set(key, value) },
  }
  return {
    storage,
    afterNextRead(key: string, write: () => void) {
      afterRead = readKey => {
        if (readKey !== key) return
        afterRead = undefined
        write()
      }
    },
  }
}

let controlled: ReturnType<typeof controlledStorage>
async function owner() {
  const module = await import('../../utils/tokenStorage')
  expect(module.setToken(firstToken, 'reader')).toBe(true)
  expect(module.setSession({ userId: 'reader', username: 'reader', email: 'reader@example.test' })).toBe(true)
  return { module, snapshot: module.captureSessionContinuity() }
}

describe('session snapshot read-order races', () => {
  beforeEach(() => {
    vi.resetModules()
    controlled = controlledStorage()
    vi.stubGlobal('localStorage', controlled.storage)
  })
  afterEach(() => vi.unstubAllGlobals())

  it.each(readKeys)('rejects a session break published after reading %s', async key => {
    const { module, snapshot } = await owner()
    let injected = false
    controlled.afterNextRead(key, () => {
      injected = true
      controlled.storage.setItem('taskdeck_session_break', 'different-user-break')
      controlled.storage.setItem('taskdeck_token', secondToken)
      // The other tab has not replaced session metadata yet; both reads still
      // identify reader. The new break, not that old metadata, must retire it.
    })

    expect(module.isSameSessionContinuity(snapshot)).toBe(false)
    expect(injected).toBe(true)
  })

  it.each(readKeys)('preserves same-user refresh after reading %s', async key => {
    const { module, snapshot } = await owner()
    let injected = false
    controlled.afterNextRead(key, () => {
      injected = true
      controlled.storage.setItem('taskdeck_token', secondToken)
    })

    expect(module.isSameSessionContinuity(snapshot)).toBe(true)
    expect(injected).toBe(true)
  })
})
