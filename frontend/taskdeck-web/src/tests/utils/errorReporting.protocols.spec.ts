import { afterEach, describe, expect, it, vi } from 'vitest'
import { reportToSentry } from '../../utils/errorReporting'

describe('Sentry request URL protocol boundary', () => {
  afterEach(() => vi.unstubAllGlobals())

  it.each([
    'data:text/plain,TOPSECRET?token=x',
    'DATA:text/plain,TOPSECRET',
    '  data:text/plain,TOPSECRET',
    'file:///home/TOPSECRET/private.txt',
    'blob:https://api.example.test/TOPSECRET',
    'javascript:TOPSECRET()',
    'mailto:TOPSECRET@example.test',
    'ftp://api.example.test/TOPSECRET',
    'C:\\Users\\TOPSECRET\\private.txt',
    'http://[TOPSECRET',
  ])('omits opaque, local, unsupported, or malformed URL %s', (url) => {
    const captureException = vi.fn()
    vi.stubGlobal('Sentry', { captureException })

    expect(reportToSentry({
      isAxiosError: true,
      config: { method: 'get', url },
      response: { status: 500 },
    })).toBe(true)

    const forwarded = captureException.mock.calls[0]?.[0] as Error & {
      sentryContext: { path?: string }
    }
    expect(forwarded).toBeInstanceOf(Error)
    expect(forwarded.message).toBe('Request failed (status 500 GET)')
    expect(forwarded.sentryContext.path).toBeUndefined()
    expect(`${forwarded.message} ${JSON.stringify(forwarded)}`).not.toContain('TOPSECRET')
  })

  it.each([
    ['/api/cards?token=TOPSECRET#TOPSECRET', '/api/cards'],
    ['https://user:TOPSECRET@api.example.test/api/cards?token=TOPSECRET#TOPSECRET', 'https://api.example.test/api/cards'],
    ['//user:TOPSECRET@api.example.test/api/cards?token=TOPSECRET#TOPSECRET', 'http://api.example.test/api/cards'],
    ['api/cards?token=TOPSECRET', '/api/cards'],
  ])('preserves safe HTTP(S) request coordinates for %s', (url, path) => {
    const captureException = vi.fn()
    vi.stubGlobal('Sentry', { captureException })

    expect(reportToSentry({ isAxiosError: true, config: { url } })).toBe(true)

    const forwarded = captureException.mock.calls[0]?.[0] as Error & {
      sentryContext: { path?: string }
    }
    expect(forwarded.sentryContext.path).toBe(path)
    expect(`${forwarded.message} ${JSON.stringify(forwarded)}`).not.toContain('TOPSECRET')
  })
})
