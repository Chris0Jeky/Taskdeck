import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearStaleBundleRecoveryMarker, installStaleBundleRecovery } from '../../pwa/staleBundleRecovery'

function firePreloadError(): Event {
  const event = new Event('vite:preloadError', { cancelable: true })
  window.dispatchEvent(event)
  return event
}

describe('stale bundle recovery', () => {
  beforeEach(() => {
    sessionStorage.clear()
  })

  afterEach(() => {
    sessionStorage.clear()
  })

  it('reloads once when a route chunk from the retired bundle cannot be fetched', () => {
    // The API-cache migration activates a replacement worker under a running page and
    // cleanupOutdatedCaches drops the old precache, so a lazy chunk named with the
    // previous build hash resolves to a URL nothing serves any more.
    const reload = vi.fn()
    const stop = installStaleBundleRecovery(reload)

    const event = firePreloadError()

    expect(reload).toHaveBeenCalledTimes(1)
    expect(event.defaultPrevented).toBe(true)
    stop()
  })

  it('does not reload again after the first attempt, so a broken deploy cannot loop', () => {
    const reload = vi.fn()
    const stop = installStaleBundleRecovery(reload)

    firePreloadError()
    firePreloadError()

    expect(reload).toHaveBeenCalledTimes(1)
    stop()
  })

  it('clears the guard once the app has mounted so a later deploy can recover again', () => {
    const reload = vi.fn()
    const stop = installStaleBundleRecovery(reload)

    firePreloadError()
    clearStaleBundleRecoveryMarker()
    firePreloadError()

    expect(reload).toHaveBeenCalledTimes(2)
    stop()
  })

  it('does not reload when recovery state cannot be stored', () => {
    // Without a marker the first attempt cannot be told from the tenth, and a reload
    // loop is worse than a failed navigation.
    const reload = vi.fn()
    const getItem = vi.spyOn(window.sessionStorage, 'getItem').mockImplementation(() => {
      throw new Error('storage disabled')
    })
    const stop = installStaleBundleRecovery(reload)

    firePreloadError()

    expect(reload).not.toHaveBeenCalled()
    getItem.mockRestore()
    stop()
  })

  it('stops listening once torn down', () => {
    const reload = vi.fn()
    installStaleBundleRecovery(reload)()

    firePreloadError()

    expect(reload).not.toHaveBeenCalled()
  })
})
