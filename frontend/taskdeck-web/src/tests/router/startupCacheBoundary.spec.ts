import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import * as tokenStorage from '../../utils/tokenStorage'

const cachePurgeMocks = vi.hoisted(() => ({ purge: vi.fn() }))

vi.mock('../../pwa/legacyApiCache', () => ({
  purgeLegacyApiCaches: cachePurgeMocks.purge,
}))

function fakeJwt(): string {
  const encode = (value: unknown) => btoa(JSON.stringify(value)).replace(/=+$/g, '')
  return `${encode({ alg: 'HS256', typ: 'JWT' })}.${encode({ exp: Math.floor(Date.now() / 1000) + 3600 })}.signature`
}

describe('startup API-cache boundary', () => {
  beforeEach(() => {
    vi.resetModules()
    vi.clearAllMocks()
    setActivePinia(createPinia())
    localStorage.clear()
  })

  it('waits to read persisted credentials until the initial cache purge finishes', async () => {
    let release: (result: boolean) => void = () => undefined
    cachePurgeMocks.purge.mockReturnValue(new Promise<boolean>((resolve) => { release = resolve }))
    tokenStorage.setToken(fakeJwt())
    const getToken = vi.spyOn(tokenStorage, 'getToken')
    const { default: router } = await import('../../router')

    const navigation = router.push('/workspace/home')
    await Promise.resolve()
    expect(getToken).not.toHaveBeenCalled()

    release(true)
    await navigation
    expect(router.currentRoute.value.path).toBe('/workspace/home')

    await router.push('/workspace/today')
    expect(cachePurgeMocks.purge).toHaveBeenCalledTimes(1)
  })

  it('retries a failed startup purge before a later protected navigation', async () => {
    cachePurgeMocks.purge
      .mockResolvedValueOnce(false)
      .mockResolvedValueOnce(true)
    tokenStorage.setToken(fakeJwt())
    const { default: router } = await import('../../router')

    await router.push('/workspace/home')
    expect(router.currentRoute.value.path).toBe('/login')

    tokenStorage.setToken(fakeJwt())
    await router.push('/workspace/home')
    expect(router.currentRoute.value.path).toBe('/workspace/home')
    expect(cachePurgeMocks.purge).toHaveBeenCalledTimes(2)
  })
})
