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
