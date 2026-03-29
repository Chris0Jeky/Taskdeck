import { describe, expect, it, beforeEach } from 'vitest'
import {
  isValidJwtStructure,
  validateSessionData,
  getToken,
  setToken,
  removeToken,
  getSession,
  setSession,
  removeSession,
  clearAll,
} from '../../utils/tokenStorage'

function toBase64Url(value: string): string {
  return btoa(value).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '')
}

function createFakeJwt(payload: Record<string, unknown> = { sub: 'user-1' }): string {
  const header = toBase64Url(JSON.stringify({ alg: 'HS256', typ: 'JWT' }))
  const body = toBase64Url(JSON.stringify(payload))
  return `${header}.${body}.fakesignature`
}

describe('isValidJwtStructure', () => {
  it('accepts a well-formed three-part JWT', () => {
    expect(isValidJwtStructure(createFakeJwt())).toBe(true)
  })

  it('rejects empty string', () => {
    expect(isValidJwtStructure('')).toBe(false)
  })

  it('rejects non-string values', () => {
    expect(isValidJwtStructure(null as unknown as string)).toBe(false)
    expect(isValidJwtStructure(undefined as unknown as string)).toBe(false)
    expect(isValidJwtStructure(123 as unknown as string)).toBe(false)
  })

  it('rejects tokens with fewer than three parts', () => {
    expect(isValidJwtStructure('header.payload')).toBe(false)
    expect(isValidJwtStructure('single')).toBe(false)
  })

  it('rejects tokens with more than three parts', () => {
    expect(isValidJwtStructure('a.b.c.d')).toBe(false)
  })

  it('rejects tokens with empty parts', () => {
    expect(isValidJwtStructure('.payload.sig')).toBe(false)
    expect(isValidJwtStructure('header..sig')).toBe(false)
    expect(isValidJwtStructure('header.payload.')).toBe(false)
  })

  it('rejects tokens with invalid base64url characters', () => {
    expect(isValidJwtStructure('head er.payload.sig')).toBe(false)
    expect(isValidJwtStructure('header.pay=load.sig')).toBe(false)
  })

  it('rejects tokens exceeding maximum length', () => {
    const longPart = 'a'.repeat(2000)
    expect(isValidJwtStructure(`${longPart}.${longPart}.${longPart}`)).toBe(false)
  })
})

describe('validateSessionData', () => {
  const validSession = {
    userId: 'user-123',
    username: 'alice',
    email: 'alice@example.com',
  }

  it('accepts valid session data without defaultRole', () => {
    const result = validateSessionData(validSession)
    expect(result).toEqual({ ...validSession, defaultRole: undefined })
  })

  it('accepts valid session data with numeric defaultRole', () => {
    const result = validateSessionData({ ...validSession, defaultRole: 1 })
    expect(result).toEqual({ ...validSession, defaultRole: 1 })
  })

  it('rejects null input', () => {
    expect(validateSessionData(null)).toBeNull()
  })

  it('rejects non-object input', () => {
    expect(validateSessionData('string')).toBeNull()
    expect(validateSessionData(42)).toBeNull()
  })

  it('rejects missing userId', () => {
    expect(validateSessionData({ username: 'a', email: 'b' })).toBeNull()
  })

  it('rejects empty userId', () => {
    expect(validateSessionData({ userId: '', username: 'a', email: 'b' })).toBeNull()
  })

  it('rejects non-string userId', () => {
    expect(validateSessionData({ userId: 123, username: 'a', email: 'b' })).toBeNull()
  })

  it('rejects missing username', () => {
    expect(validateSessionData({ userId: 'x', email: 'b' })).toBeNull()
  })

  it('rejects missing email', () => {
    expect(validateSessionData({ userId: 'x', username: 'a' })).toBeNull()
  })

  it('rejects non-numeric defaultRole', () => {
    expect(validateSessionData({ ...validSession, defaultRole: 'admin' })).toBeNull()
  })

  it('rejects fields exceeding maximum length', () => {
    const longValue = 'a'.repeat(513)
    expect(validateSessionData({ ...validSession, userId: longValue })).toBeNull()
    expect(validateSessionData({ ...validSession, username: longValue })).toBeNull()
    expect(validateSessionData({ ...validSession, email: longValue })).toBeNull()
  })
})

describe('token storage operations', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it('setToken stores a valid JWT and getToken retrieves it', () => {
    const jwt = createFakeJwt()
    expect(setToken(jwt)).toBe(true)
    expect(getToken()).toBe(jwt)
  })

  it('setToken rejects an invalid token', () => {
    expect(setToken('not-a-jwt')).toBe(false)
    expect(getToken()).toBeNull()
  })

  it('getToken returns null when nothing is stored', () => {
    expect(getToken()).toBeNull()
  })

  it('getToken removes and returns null for a corrupted stored value', () => {
    localStorage.setItem('taskdeck_token', 'corrupted-value')
    expect(getToken()).toBeNull()
    expect(localStorage.getItem('taskdeck_token')).toBeNull()
  })

  it('removeToken clears the stored token', () => {
    setToken(createFakeJwt())
    removeToken()
    expect(getToken()).toBeNull()
  })
})

describe('session storage operations', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  const validSession = {
    userId: 'user-1',
    username: 'alice',
    email: 'alice@test.com',
  }

  it('setSession stores and getSession retrieves valid data', () => {
    expect(setSession(validSession)).toBe(true)
    const restored = getSession()
    expect(restored).toEqual({ ...validSession, defaultRole: undefined })
  })

  it('setSession with defaultRole stores and retrieves correctly', () => {
    const session = { ...validSession, defaultRole: 2 }
    expect(setSession(session)).toBe(true)
    expect(getSession()?.defaultRole).toBe(2)
  })

  it('setSession rejects invalid data', () => {
    expect(setSession({ userId: '', username: 'a', email: 'b' })).toBe(false)
  })

  it('getSession returns null when nothing is stored', () => {
    expect(getSession()).toBeNull()
  })

  it('getSession returns null and cleans up corrupted JSON', () => {
    localStorage.setItem('taskdeck_session', '{invalid json')
    expect(getSession()).toBeNull()
    expect(localStorage.getItem('taskdeck_session')).toBeNull()
  })

  it('getSession returns null for valid JSON with invalid shape', () => {
    localStorage.setItem('taskdeck_session', JSON.stringify({ wrong: 'shape' }))
    expect(getSession()).toBeNull()
  })

  it('removeSession clears the stored session', () => {
    setSession(validSession)
    removeSession()
    expect(getSession()).toBeNull()
  })
})

describe('clearAll', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it('removes both token and session', () => {
    setToken(createFakeJwt())
    setSession({ userId: 'u', username: 'a', email: 'e' })
    clearAll()
    expect(getToken()).toBeNull()
    expect(getSession()).toBeNull()
  })
})
