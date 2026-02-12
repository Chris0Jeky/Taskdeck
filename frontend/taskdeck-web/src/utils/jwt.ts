export interface JwtPayload {
  exp?: number
}

function decodeBase64Url(value: string): string | null {
  try {
    const normalized = value.replace(/-/g, '+').replace(/_/g, '/')
    const paddingLength = (4 - (normalized.length % 4)) % 4
    const padded = normalized + '='.repeat(paddingLength)
    return atob(padded)
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
    const payload = JSON.parse(decoded) as JwtPayload
    return payload
  } catch {
    return null
  }
}

export function getTokenExpiryIso(token: string): string | null {
  const payload = parseJwtPayload(token)
  if (!payload?.exp) return null
  return new Date(payload.exp * 1000).toISOString()
}

export function isTokenExpired(token: string): boolean {
  const payload = parseJwtPayload(token)
  if (!payload?.exp) return false
  return Date.now() >= payload.exp * 1000
}
