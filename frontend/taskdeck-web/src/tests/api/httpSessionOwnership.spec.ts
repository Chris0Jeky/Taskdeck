/** Real Axios interceptor regressions for request-owned credentials (#3317). */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import axios, { AxiosHeaders } from 'axios'
import MockAdapter from 'axios-mock-adapter'

const effects = vi.hoisted(() => ({ purge: vi.fn(), expired: vi.fn() }))
vi.mock('../../utils/demoMode', () => ({ isDemoMode: false }))
vi.mock('../../utils/errorReporting', () => ({ logError: vi.fn(), logWarn: vi.fn() }))
vi.mock('../../utils/authExpiry', () => ({ notifyAuthExpired: effects.expired }))
vi.mock('../../pwa/legacyApiCache', () => ({ purgeLegacyApiCaches: effects.purge }))

import http from '../../api/http'
import * as tokenStorage from '../../utils/tokenStorage'

function normalizedHeaders(value: unknown): AxiosHeaders {
  if (!(value instanceof AxiosHeaders)) throw new Error('Expected normalized Axios request headers')
  return value
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => { resolve = done })
  return { promise, resolve }
}

function jwt(subject: string, seconds = 3600): string {
  const encode = (value: object) => btoa(JSON.stringify(value))
    .replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '')
  return `${encode({ alg: 'HS256', typ: 'JWT' })}.${encode({
    sub: subject, exp: Math.floor(Date.now() / 1000) + seconds,
  })}.synthetic`
}

function signIn(subject: string, token = jwt(subject)): string {
  expect(tokenStorage.setToken(token)).toBe(true)
  expect(tokenStorage.setSession({ userId: subject, username: subject, email: `${subject}@example.test` })).toBe(true)
  return token
}

describe('HTTP session ownership', () => {
  let mock: MockAdapter
  const originalLocation = window.location

  beforeEach(() => {
    vi.useFakeTimers()
    tokenStorage.clearAll()
    effects.purge.mockReset().mockResolvedValue(true)
    effects.expired.mockReset()
    mock = new MockAdapter(http)
    Object.defineProperty(window, 'location', {
      value: { ...originalLocation, href: 'http://localhost/workspace/home', pathname: '/workspace/home', search: '' },
      configurable: true,
    })
  })

  afterEach(() => {
    mock.restore()
    vi.useRealTimers()
    Object.defineProperty(window, 'location', { value: originalLocation, configurable: true })
    tokenStorage.clearAll()
  })

  function pendingReply() {
    const started = deferred<void>()
    const reply = deferred<[number, object]>()
    mock.onAny('/session-owned').replyOnce(() => {
      started.resolve()
      return reply.promise
    })
    return { started, reply }
  }

  it.each(['different-token', 'same-token', 'anonymous', 'external-storage'] as const)(
    'a late %s 401 cannot expire a replacement session', async (kind) => {
      const firstToken = jwt('first')
      if (kind !== 'anonymous') signIn('first', firstToken)
      const { started, reply } = pendingReply()
      const outcome = http.get('/session-owned', { skipRetry: true }).catch((error: unknown) => error)
      await started.promise

      let currentToken: string
      if (kind === 'external-storage') {
        currentToken = jwt('second')
        localStorage.setItem('taskdeck_token', currentToken)
        localStorage.setItem('taskdeck_session', JSON.stringify({
          userId: 'second', username: 'second', email: 'second@example.test',
        }))
      } else {
        tokenStorage.clearAll()
        currentToken = signIn('second', kind === 'same-token' ? firstToken : jwt('second'))
      }
      reply.resolve([401, {}])
      expect(await outcome).toMatchObject({ response: { status: 401 } })
      expect(tokenStorage.getToken()).toBe(currentToken)
      expect(tokenStorage.getSession()?.userId).toBe('second')
      expect(effects.purge).not.toHaveBeenCalled()
      expect(effects.expired).not.toHaveBeenCalled()
      expect(window.location.href).toBe('http://localhost/workspace/home')
    },
  )

  it('still expires and redirects a request belonging to the current session', async () => {
    signIn('first')
    mock.onGet('/session-owned').reply(401, {})
    await expect(http.get('/session-owned')).rejects.toMatchObject({ response: { status: 401 } })
    expect(tokenStorage.getToken()).toBeNull()
    expect(tokenStorage.getSession()).toBeNull()
    expect(effects.purge).toHaveBeenCalledOnce()
    expect(effects.expired).toHaveBeenCalledOnce()
    expect(window.location.href).toBe('/login?redirect=%2Fworkspace%2Fhome')
  })

  describe.each(['get', 'put', 'delete'] as const)('%s retries', (method) => {
    it.each(['before-response', 'during-backoff'] as const)(
      'never dispatch with replacement credentials after a change %s', async (stage) => {
        signIn('first')
        const { started, reply } = pendingReply()
        mock.onAny('/session-owned').reply(200, { ok: true })
        const outcome = http.request({ url: '/session-owned', method }).catch((error: unknown) => error)
        await started.promise

        if (stage === 'during-backoff') {
          reply.resolve([503, {}])
          await vi.advanceTimersByTimeAsync(0)
          expect(vi.getTimerCount()).toBeGreaterThan(0)
        }
        const currentToken = signIn('second')
        if (stage === 'before-response') reply.resolve([503, {}])
        await vi.runAllTimersAsync()

        expect(axios.isCancel(await outcome)).toBe(true)
        expect(mock.history[method]).toHaveLength(1)
        expect(tokenStorage.getToken()).toBe(currentToken)
        expect(effects.expired).not.toHaveBeenCalled()
      },
    )
  })

  it.each(['logout', 'same-token-login'] as const)(
    'invalidates a pending retry on %s even without a different token string', async (change) => {
      const token = signIn('first')
      mock.onGet('/session-owned').replyOnce(503, {})
      mock.onGet('/session-owned').reply(200, {})
      const outcome = http.get('/session-owned').catch((error: unknown) => error)
      await vi.advanceTimersByTimeAsync(0)
      expect(vi.getTimerCount()).toBeGreaterThan(0)
      tokenStorage.clearAll()
      if (change === 'same-token-login') signIn('first', token)
      await vi.runAllTimersAsync()
      expect(axios.isCancel(await outcome)).toBe(true)
      expect(mock.history.get).toHaveLength(1)
      expect(tokenStorage.getToken()).toBe(change === 'logout' ? null : token)
    },
  )

  it('preserves same-session retries, bearer identity and request id', async () => {
    const token = signIn('first')
    mock.onGet('/session-owned').replyOnce(503, {})
    mock.onGet('/session-owned').reply(200, { ok: true })
    const request = http.get('/session-owned')
    await vi.runAllTimersAsync()
    expect((await request).data).toEqual({ ok: true })
    expect(mock.history.get).toHaveLength(2)
    const first = normalizedHeaders(mock.history.get[0]!.headers)
    const second = normalizedHeaders(mock.history.get[1]!.headers)
    expect(second.get('Authorization')).toBe(`Bearer ${token}`)
    expect(second.get('X-Request-Id')).toBe(first.get('X-Request-Id'))
    expect(effects.expired).not.toHaveBeenCalled()
  })

  it.each(['absent', 'expired'] as const)(
    'removes inherited Authorization when the stored credential is %s', async (state) => {
      if (state === 'expired') tokenStorage.setToken(jwt('expired', -60))
      mock.onGet('/session-owned').reply(200, {})
      await http.get('/session-owned', { headers: { Authorization: 'Bearer obsolete' } })
      expect(normalizedHeaders(mock.history.get[0]!.headers).get('Authorization')).toBeUndefined()
      expect(tokenStorage.getToken()).toBeNull()
    },
  )
})
