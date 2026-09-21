export interface JwtPayload {
  exp?: number
}

function decodeBase64Url(value: string): string | null {
  try {
    const normalized = value.replace(/-/g, '+').replace(/_/g, '/')
    const paddingLength = (4 - (normalized.length % 4)) % 4
    const padded = normalized + '='.repeat(paddingLength)
    const bytes = Uint8Array.from(atob(padded), char => char.charCodeAt(0))
    return new TextDecoder('utf-8', { fatal: true }).decode(bytes)
  } catch {
    return null
  }
}

export function parseJwtPayload(token: string): JwtPayload | null {
  const parts = token.split('.')
  const payloadPart = parts[1]
  if (!payloadPart) return null

  const decoded = decodeBase64Url(payloadPart)
  if (!decoded) return null

  try {
    const payload: unknown = JSON.parse(decoded)
    if (!payload || typeof payload !== 'object' || Array.isArray(payload)) return null

    // An optional NumericDate must be usable by every session consumer, including
    // ISO formatting. Invalid claims must not turn into a non-expiring session.
    if ('exp' in payload && (
      typeof payload.exp !== 'number'
      || !Number.isFinite(payload.exp)
      || !Number.isFinite(new Date(payload.exp * 1000).getTime())
    )) return null

    return payload as JwtPayload
  } catch {
    return null
  }
}

export function getTokenExpiryIso(token: string): string | null {
  const payload = parseJwtPayload(token)
  if (payload?.exp === undefined) return null
  return new Date(payload.exp * 1000).toISOString()
}

export function isTokenExpired(token: string): boolean {
  const payload = parseJwtPayload(token)
  if (!payload) return true
  if (payload.exp === undefined) return false
  return Date.now() >= payload.exp * 1000
}
