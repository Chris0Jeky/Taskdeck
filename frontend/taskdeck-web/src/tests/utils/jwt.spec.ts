import { describe, expect, it } from 'vitest'
import { getTokenExpiryIso, isTokenExpired, parseJwtPayload } from '../../utils/jwt'

function toBase64Url(value: string): string {
  return btoa(value).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '')
}

function createToken(payload: Record<string, unknown>): string {
  const header = toBase64Url(JSON.stringify({ alg: 'HS256', typ: 'JWT' }))
  const body = toBase64Url(JSON.stringify(payload))
  return `${header}.${body}.sig`
}

describe('jwt utils', () => {
  it('parses base64url payloads', () => {
    const token = createToken({ exp: 1893456000, custom: 'ok' })
    const payload = parseJwtPayload(token)

    expect(payload?.exp).toBe(1893456000)
  })

  it('returns null expiry when exp is missing', () => {
    const token = createToken({ sub: 'user-1' })
    expect(getTokenExpiryIso(token)).toBeNull()
  })

  it('detects expired token', () => {
    const token = createToken({ exp: Math.floor(Date.now() / 1000) - 10 })
    expect(isTokenExpired(token)).toBe(true)
  })

  it('treats token without exp as not expired', () => {
    const token = createToken({ sub: 'user-1' })
    expect(isTokenExpired(token)).toBe(false)
  })
})
