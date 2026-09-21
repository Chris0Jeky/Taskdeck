import { describe, expect, it } from 'vitest'
import { getTokenExpiryIso, isTokenExpired, parseJwtPayload } from '../../utils/jwt'

function toBase64Url(value: string): string {
  const bytes = new TextEncoder().encode(value)
  return btoa(String.fromCharCode(...bytes)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '')
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

  it('returns null for token with no payload segment', () => {
    expect(parseJwtPayload('header-only')).toBeNull()
  })

  it('returns ISO string for valid expiry', () => {
    const token = createToken({ exp: 1893456000 })
    const iso = getTokenExpiryIso(token)
    expect(iso).toBe(new Date(1893456000 * 1000).toISOString())
  })

  it('returns null for malformed base64 payload', () => {
    expect(parseJwtPayload('header.!!!invalid!!!.sig')).toBeNull()
  })
})

describe('JWT payload admission', () => {
  function tokenFromJson(json: string): string {
    return `header.${toBase64Url(json)}.sig`
  }

  it.each(['null', 'true', '42', '"text"', '[]', '[{"exp":1}]'])(
    'rejects non-object payload %s', (json) => {
      expect(parseJwtPayload(tokenFromJson(json))).toBeNull()
      expect(isTokenExpired(tokenFromJson(json))).toBe(true)
    },
  )

  it.each([
    'null', 'false', '"1893456000"', '"bad"', '[]', '[1893456000]', '{}',
    '1e400', '-1e400', '1e100', '-1e100', '8640000000001', '-8640000000001',
  ])('rejects unusable exp %s without throwing during session setup', (exp) => {
    const token = tokenFromJson(`{"exp":${exp}}`)
    expect(getTokenExpiryIso(token)).toBeNull()
    expect(parseJwtPayload(token)).toBeNull()
    expect(isTokenExpired(token)).toBe(true)
  })

  it('treats epoch zero as expired rather than absent', () => {
    const token = createToken({ exp: 0 })
    expect(getTokenExpiryIso(token)).toBe('1970-01-01T00:00:00.000Z')
    expect(isTokenExpired(token)).toBe(true)
  })

  it.each([-8640000000000, -1.25, 1893456000.125, 8640000000000])(
    'preserves a representable numeric exp %s', (exp) => {
      const token = createToken({ exp })
      expect(parseJwtPayload(token)?.exp).toBe(exp)
      expect(getTokenExpiryIso(token)).toBe(new Date(exp * 1000).toISOString())
    },
  )

  it('decodes UTF-8 claims without corrupting non-ASCII text', () => {
    const payload = { sub: 'Ștefan 日本語 🌌', exp: 1893456000 }
    expect(parseJwtPayload(createToken(payload))).toEqual(payload)
  })

  it('rejects invalid UTF-8 rather than accepting Latin-1 as JSON', () => {
    const binary = '{"sub":"' + String.fromCharCode(0xc3, 0x28) + '"}'
    const payload = btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '')
    expect(parseJwtPayload(`header.${payload}.sig`)).toBeNull()
  })

  it.each(['header-only', 'header.!!!invalid!!!.sig', tokenFromJson('{bad')])(
    'treats malformed input %s as unusable', (token) => {
      expect(getTokenExpiryIso(token)).toBeNull()
      expect(isTokenExpired(token)).toBe(true)
    },
  )
})
