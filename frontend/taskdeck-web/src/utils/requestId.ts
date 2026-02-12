interface RequestIdSources {
  now?: () => number
  random?: () => number
  randomUUID?: () => string
}

export function createRequestId(sources: RequestIdSources = {}): string {
  const hasExplicitRandomUUID = Object.prototype.hasOwnProperty.call(sources, 'randomUUID')
  const randomUUID = hasExplicitRandomUUID
    ? sources.randomUUID
    : globalThis.crypto?.randomUUID?.bind(globalThis.crypto)
  if (typeof randomUUID === 'function') {
    return randomUUID()
  }

  const now = sources.now ?? Date.now
  const random = sources.random ?? Math.random
  const timestamp = now().toString(36)
  const suffix = Math.floor(random() * 0x100000000).toString(16).padStart(8, '0')
  return `req-${timestamp}-${suffix}`
}
