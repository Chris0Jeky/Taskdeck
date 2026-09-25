import { parseJwtPayload } from './jwt'
import { createRequestId } from './requestId'

/**
 * Centralized token and session storage abstraction.
 *
 * All token/session persistence goes through this module so that
 * the underlying storage mechanism can be changed in one place
 * (e.g. migrating from localStorage to HttpOnly cookies or sessionStorage).
 */

const TOKEN_KEY = 'taskdeck_token'
const SESSION_KEY = 'taskdeck_session'
const SESSION_BREAK_KEY = 'taskdeck_session_break'

// In-memory ownership only: never persisted or sent to the server. Explicit
// token writes/removals advance even when the token string is unchanged, so
// logout followed by same-token login cannot revive an old request owner.
let credentialGeneration = 0
let observedToken: string | null = null
// Session-break generation advances on credential removal or a change of
// identity, but not on a same-user token refresh. Publishing the break before
// an identity replacement's token prevents another tab from reading a new
// token with the previous session metadata and accepting a stale verdict.
let sessionBreakGeneration = 0
let observedSessionBreakMarker: string | null = null

function advanceCredentialGeneration(token: string | null): void {
  observedToken = token
  credentialGeneration++
}

function observeCrossTabSessionBreak(): void {
  const marker = localStorage.getItem(SESSION_BREAK_KEY)
  if (marker !== observedSessionBreakMarker) {
    observedSessionBreakMarker = marker
    sessionBreakGeneration++
  }
}

function advanceSessionBreak(publish = false): void {
  sessionBreakGeneration++
  if (publish) {
    // A persisted marker lets another tab detect logout even when logout and
    // same-user login both finish before that tab next reads the token.
    const marker = createRequestId()
    localStorage.setItem(SESSION_BREAK_KEY, marker)
    observedSessionBreakMarker = marker
  }
}

/**
 * Generation of the last observed credential. Read getToken() immediately
 * before taking/checking a request snapshot; that observes external storage
 * changes too. No additional copy of the token belongs on request metadata.
 */
export function getObservedCredentialGeneration(): number {
  return credentialGeneration
}

/**
 * Generation of the last observed session break. Read
 * getToken() immediately before taking/checking a request snapshot, then
 * compare alongside getSession()?.userId: same break + same user means the
 * request still belongs to the current user, including across a same-user
 * token refresh that advances the credential generation.
 */
export function getObservedSessionBreakGeneration(): number {
  return sessionBreakGeneration
}

export interface SessionContinuity {
  breakGeneration: number
  userId: string | null
  credentialGeneration: number
}

/**
 * Snapshot the session continuity a revocation verdict must belong to. Call it
 * when the request (or subscription intent) starts; compare with
 * isSameSessionContinuity when the verdict settles. A same-user token refresh
 * keeps break + user stable, so the verdict still applies; a logout advances
 * the break and a user replacement changes the user, so stale verdicts fail.
 */
export function captureSessionContinuity(): SessionContinuity {
  getToken()
  return {
    breakGeneration: getObservedSessionBreakGeneration(),
    userId: getSession()?.userId ?? null,
    credentialGeneration: getObservedCredentialGeneration(),
  }
}

/**
 * Whether a continuity snapshot still belongs to the current session: same
 * break generation (no logout or identity replacement since) and same user. If
 * either user identity is unavailable, require the credential to be unchanged.
 */
export function isSameSessionContinuity(snapshot: SessionContinuity): boolean {
  getToken()
  const currentUserId = getSession()?.userId ?? null
  return (
    getObservedSessionBreakGeneration() === snapshot.breakGeneration &&
    (snapshot.userId !== null && currentUserId !== null
      ? currentUserId === snapshot.userId
      : getObservedCredentialGeneration() === snapshot.credentialGeneration)
  )
}

/** Maximum allowed length for a stored token string. */
const MAX_TOKEN_LENGTH = 4096

/** Maximum allowed length for individual session metadata fields. */
const MAX_SESSION_FIELD_LENGTH = 512

export interface PersistedSession {
  userId: string
  username: string
  email: string
  defaultRole?: number
}

/**
 * Validates that a string has the basic structure of a JWT (three base64url-encoded segments).
 * Checks UTF-8 object payloads and the shape/range of an optional expiry too.
 * Does not authenticate the token or verify its signature, issuer, or audience.
 */
export function isValidJwtStructure(token: string): boolean {
  if (!token || typeof token !== 'string') return false
  if (token.length > MAX_TOKEN_LENGTH) return false

  const parts = token.split('.')
  if (parts.length !== 3) return false

  // Each part must be non-empty and contain only base64url characters
  const base64urlPattern = /^[A-Za-z0-9_-]+$/
  if (!parts.every(part => part.length > 0 && base64urlPattern.test(part))) {
    return false
  }

  return parseJwtPayload(token) !== null
}

/**
 * Validates and sanitizes persisted session data.
 * Returns null if the data is invalid or contains unexpected values.
 */
export function validateSessionData(raw: unknown): PersistedSession | null {
  if (!raw || typeof raw !== 'object') return null

  const obj = raw as Record<string, unknown>

  if (typeof obj.userId !== 'string' || obj.userId.length === 0 || obj.userId.length > MAX_SESSION_FIELD_LENGTH) {
    return null
  }
  if (typeof obj.username !== 'string' || obj.username.length === 0 || obj.username.length > MAX_SESSION_FIELD_LENGTH) {
    return null
  }
  if (typeof obj.email !== 'string' || obj.email.length === 0 || obj.email.length > MAX_SESSION_FIELD_LENGTH) {
    return null
  }

  const defaultRole = obj.defaultRole
  if (defaultRole !== undefined && defaultRole !== null && typeof defaultRole !== 'number') {
    return null
  }

  return {
    userId: obj.userId,
    username: obj.username,
    email: obj.email,
    defaultRole: typeof defaultRole === 'number' ? defaultRole : undefined,
  }
}

// --- Token operations ---

export function getToken(): string | null {
  observeCrossTabSessionBreak()
  const token = localStorage.getItem(TOKEN_KEY)
  if (token && !isValidJwtStructure(token)) {
    // Corrupted or malicious value — remove it
    localStorage.removeItem(TOKEN_KEY)
    if (observedToken !== null) {
      advanceCredentialGeneration(null)
      advanceSessionBreak(true)
    }
    return null
  }
  if (token !== observedToken) {
    const removalObserved = token === null && observedToken !== null
    advanceCredentialGeneration(token)
    if (removalObserved) advanceSessionBreak()
  }
  return token
}

export function setToken(token: string, sessionUserId?: string): boolean {
  if (!isValidJwtStructure(token)) {
    return false
  }
  // setSession writes token and metadata separately. Publish the identity
  // break first so another tab cannot accept a stale result between writes.
  if (sessionUserId !== undefined && getSession()?.userId !== sessionUserId) {
    advanceSessionBreak(true)
  }
  localStorage.setItem(TOKEN_KEY, token)
  advanceCredentialGeneration(token)
  return true
}

export function removeToken(): void {
  localStorage.removeItem(TOKEN_KEY)
  advanceCredentialGeneration(null)
  advanceSessionBreak(true)
}

// --- Session metadata operations ---

export function getSession(): PersistedSession | null {
  const raw = localStorage.getItem(SESSION_KEY)
  if (!raw) return null

  try {
    const parsed = JSON.parse(raw) as unknown
    return validateSessionData(parsed)
  } catch {
    // Corrupted data — clean up
    localStorage.removeItem(SESSION_KEY)
    return null
  }
}

export function setSession(session: PersistedSession): boolean {
  if (!validateSessionData(session)) {
    return false
  }
  localStorage.setItem(SESSION_KEY, JSON.stringify(session))
  return true
}

export function removeSession(): void {
  localStorage.removeItem(SESSION_KEY)
}

// --- Bulk operations ---

export function clearAll(): void {
  removeToken()
  removeSession()
}
