import { beforeEach, describe, expect, it } from 'vitest'
import * as storage from '../../utils/tokenStorage'

const first = 'eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJmaXJzdCJ9.synthetic'
const second = 'eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJzZWNvbmQifQ.synthetic'

function observedGeneration(): number {
  storage.getToken()
  return storage.getObservedCredentialGeneration()
}

describe('credential generation', () => {
  beforeEach(() => storage.clearAll())

  it('keeps session continuity across a same-user token refresh', () => {
    storage.setToken(first)
    storage.setSession({ userId: 'user-a', username: 'ann', email: 'a@example.test' })
    const snapshot = storage.captureSessionContinuity()
    storage.setToken(second)
    storage.setSession({ userId: 'user-a', username: 'ann', email: 'a@example.test' })
    expect(storage.isSameSessionContinuity(snapshot)).toBe(true)
  })

  it('keeps continuity when another tab replaces a same-user token without logout', () => {
    storage.setToken(first)
    storage.setSession({ userId: 'user-a', username: 'ann', email: 'a@example.test' })
    const snapshot = storage.captureSessionContinuity()
    localStorage.setItem('taskdeck_token', second)
    expect(storage.isSameSessionContinuity(snapshot)).toBe(true)
  })

  it('detects another tab logging out and back in before the next token read', () => {
    storage.setToken(first)
    storage.setSession({ userId: 'user-a', username: 'ann', email: 'a@example.test' })
    const snapshot = storage.captureSessionContinuity()
    localStorage.removeItem('taskdeck_token')
    const priorBreak = localStorage.getItem('taskdeck_session_break')
    localStorage.setItem('taskdeck_session_break', `${priorBreak ?? ''}:other-tab-logout`)
    localStorage.setItem('taskdeck_token', second)
    localStorage.setItem('taskdeck_session', JSON.stringify({
      userId: 'user-a', username: 'ann', email: 'a@example.test',
    }))
    expect(storage.isSameSessionContinuity(snapshot)).toBe(false)
  })

  it('rejects a token replacement when session identity is unavailable', () => {
    storage.setToken(first)
    const snapshot = storage.captureSessionContinuity()
    storage.setToken(second)
    expect(storage.isSameSessionContinuity(snapshot)).toBe(false)
  })

  it('breaks continuity on logout even when the same user signs back in', () => {
    storage.setToken(first)
    storage.setSession({ userId: 'user-a', username: 'ann', email: 'a@example.test' })
    const snapshot = storage.captureSessionContinuity()
    storage.clearAll()
    storage.setToken(second)
    storage.setSession({ userId: 'user-a', username: 'ann', email: 'a@example.test' })
    expect(storage.isSameSessionContinuity(snapshot)).toBe(false)
  })

  it('breaks continuity when a different user replaces the session', () => {
    storage.setToken(first)
    storage.setSession({ userId: 'user-a', username: 'ann', email: 'a@example.test' })
    const snapshot = storage.captureSessionContinuity()
    storage.setToken(second)
    storage.setSession({ userId: 'user-b', username: 'bo', email: 'b@example.test' })
    expect(storage.isSameSessionContinuity(snapshot)).toBe(false)
  })

  it('does not advance the session break on a direct token replacement', () => {
    storage.setToken(first)
    storage.getToken()
    const before = storage.getObservedSessionBreakGeneration()
    storage.setToken(second)
    storage.getToken()
    expect(storage.getObservedSessionBreakGeneration()).toBe(before)
    storage.removeToken()
    storage.getToken()
    expect(storage.getObservedSessionBreakGeneration()).toBeGreaterThan(before)
  })

  it('keeps repeated reads in the same generation', () => {
    storage.setToken(first)
    const before = observedGeneration()
    expect(observedGeneration()).toBe(before)
  })

  it('invalidates owners when the same token is explicitly reinstalled', () => {
    storage.setToken(first)
    const before = observedGeneration()
    storage.setToken(first)
    expect(observedGeneration()).toBeGreaterThan(before)
  })

  it('distinguishes logout and same-token re-login', () => {
    storage.setToken(first)
    const before = observedGeneration()
    storage.clearAll()
    const loggedOut = observedGeneration()
    storage.setToken(first)
    expect(loggedOut).toBeGreaterThan(before)
    expect(observedGeneration()).toBeGreaterThan(loggedOut)
  })

  it('does not invalidate on rejected token writes or session metadata updates', () => {
    storage.setToken(first)
    const before = observedGeneration()
    expect(storage.setToken('invalid')).toBe(false)
    storage.setSession({ userId: 'first', username: 'renamed', email: 'first@example.test' })
    expect(observedGeneration()).toBe(before)
  })

  it('observes external replacement and removal on the next token read', () => {
    storage.setToken(first)
    const before = observedGeneration()
    localStorage.setItem('taskdeck_token', second)
    expect(storage.getToken()).toBe(second)
    const replaced = storage.getObservedCredentialGeneration()
    expect(replaced).toBeGreaterThan(before)
    localStorage.removeItem('taskdeck_token')
    expect(observedGeneration()).toBeGreaterThan(replaced)
  })

  it('invalidates the observed credential when malformed storage is removed', () => {
    storage.setToken(first)
    const before = observedGeneration()
    localStorage.setItem('taskdeck_token', 'invalid')
    expect(storage.getToken()).toBeNull()
    expect(storage.getObservedCredentialGeneration()).toBeGreaterThan(before)
    expect(localStorage.getItem('taskdeck_token')).toBeNull()
  })
})
