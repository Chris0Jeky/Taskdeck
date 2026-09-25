import { beforeEach, describe, expect, it, vi } from 'vitest'

const firstToken = 'eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJmaXJzdCJ9.synthetic'
const secondToken = 'eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJzZWNvbmQifQ.synthetic'
type StorageModule = typeof import('../../utils/tokenStorage')

async function freshTab(): Promise<StorageModule> {
  // Independent module-level observation state, but the same browser storage.
  // Retained imports still model the other tab's pending request snapshot.
  vi.resetModules()
  return import('../../utils/tokenStorage')
}
function signIn(storage: StorageModule, token = firstToken) {
  expect(storage.setToken(token, 'reader')).toBe(true)
  expect(storage.setSession({ userId: 'reader', username: 'reader', email: 'reader@example.test' })).toBe(true)
}

describe('cold-tab invalid credential cleanup', () => {
  beforeEach(() => { localStorage.clear(); vi.resetModules() })

  it('retires an older tab owner when cold cleanup precedes same-user re-login', async () => {
    const first = await freshTab()
    signIn(first)
    const owner = first.captureSessionContinuity()
    localStorage.setItem('taskdeck_token', 'corrupt')

    const cold = await freshTab()
    expect(cold.getToken()).toBeNull()
    signIn(cold, secondToken)

    expect(first.isSameSessionContinuity(owner)).toBe(false)
  })

  it('publishes one break when a cold tab removes a malformed persisted token', async () => {
    localStorage.setItem('taskdeck_token', 'corrupt')
    const cold = await freshTab()

    expect(cold.getToken()).toBeNull()

    const marker = localStorage.getItem('taskdeck_session_break')
    expect(marker).toBeTruthy()
    expect(cold.getObservedCredentialGeneration()).toBeGreaterThan(0)
    expect(cold.getToken()).toBeNull()
    expect(localStorage.getItem('taskdeck_session_break')).toBe(marker)
  })

  it('preserves continuity when a cold tab reads a valid token', async () => {
    const first = await freshTab()
    signIn(first)
    const owner = first.captureSessionContinuity()
    const marker = localStorage.getItem('taskdeck_session_break')
    const cold = await freshTab()

    expect(cold.getToken()).toBe(firstToken)

    expect(localStorage.getItem('taskdeck_session_break')).toBe(marker)
    expect(first.isSameSessionContinuity(owner)).toBe(true)
  })

  it('continues to retire a warm tab owner on corrupt-token removal', async () => {
    const first = await freshTab()
    signIn(first)
    const owner = first.captureSessionContinuity()
    localStorage.setItem('taskdeck_token', 'corrupt')
    expect(first.getToken()).toBeNull()
    signIn(first, secondToken)

    expect(first.isSameSessionContinuity(owner)).toBe(false)
  })
})
