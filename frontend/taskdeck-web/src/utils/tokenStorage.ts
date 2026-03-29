/**
 * Centralized token and session storage abstraction.
 *
 * All token/session persistence goes through this module so that
 * the underlying storage mechanism can be changed in one place
 * (e.g. migrating from localStorage to HttpOnly cookies or sessionStorage).
 */

const TOKEN_KEY = 'taskdeck_token'
const SESSION_KEY = 'taskdeck_session'

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
 * This is a structural check only — it does not verify signatures or claims.
 */
export function isValidJwtStructure(token: string): boolean {
  if (!token || typeof token !== 'string') return false
  if (token.length > MAX_TOKEN_LENGTH) return false

  const parts = token.split('.')
  if (parts.length !== 3) return false

  // Each part must be non-empty and contain only base64url characters
  const base64urlPattern = /^[A-Za-z0-9_-]+$/
  return parts.every(part => part.length > 0 && base64urlPattern.test(part))
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
  const token = localStorage.getItem(TOKEN_KEY)
  if (token && !isValidJwtStructure(token)) {
    // Corrupted or malicious value — remove it
    localStorage.removeItem(TOKEN_KEY)
    return null
  }
  return token
}

export function setToken(token: string): boolean {
  if (!isValidJwtStructure(token)) {
    return false
  }
  localStorage.setItem(TOKEN_KEY, token)
  return true
}

export function removeToken(): void {
  localStorage.removeItem(TOKEN_KEY)
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
