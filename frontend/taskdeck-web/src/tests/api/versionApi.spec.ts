import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AxiosResponse } from 'axios'
import http from '../../api/http'
import { apiRootFrom, resolveApiRoot, versionApi } from '../../api/versionApi'

// The repo's `.env` pins `VITE_API_BASE_URL=http://localhost:5000/api`, and Vite
// inlines it into this build, so the request the suite must see is fully
// determined. Asserting the literal — rather than re-deriving it with the
// function under test — is what makes a broken derivation fail here.
const EXPECTED_API_ROOT = 'http://localhost:5000'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
  },
}))

function healthResponse(payload: unknown): AxiosResponse {
  return {
    data: payload,
    status: 200,
    statusText: 'OK',
    headers: {},
    config: {},
  } as AxiosResponse
}

describe('versionApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('reads the version the running backend reports from /health/live', async () => {
    // Arbitrary, non-shipping value: only a real read of the response can
    // produce it, so a literal baked into the frontend could never pass.
    vi.mocked(http.get).mockResolvedValue(
      healthResponse({ status: 'Healthy', version: '9.99.0-guard', timestamp: '2026-08-22T00:00:00Z' }),
    )

    await expect(versionApi.getProductVersion()).resolves.toBe('9.99.0-guard')
  })

  it('targets the server root outside the /api prefix, and fails fast', async () => {
    vi.mocked(http.get).mockResolvedValue(healthResponse({ version: '0.1.1' }))

    await versionApi.getProductVersion()

    expect(http.get).toHaveBeenCalledWith('/health/live', {
      baseURL: EXPECTED_API_ROOT,
      skipRetry: true,
    })
  })

  it.each([
    ['a missing version field', {}],
    ['a blank version field', { version: '   ' }],
    ['a non-string version field', { version: 42 }],
    ['an empty payload', null],
  ])('returns null for %s rather than inventing a value', async (_case, payload) => {
    vi.mocked(http.get).mockResolvedValue(healthResponse(payload))

    await expect(versionApi.getProductVersion()).resolves.toBeNull()
  })

  it('trims surrounding whitespace from the reported version', async () => {
    vi.mocked(http.get).mockResolvedValue(healthResponse({ version: ' 0.1.1 ' }))

    await expect(versionApi.getProductVersion()).resolves.toBe('0.1.1')
  })

  it('propagates transport failures so the caller decides what unknown looks like', async () => {
    vi.mocked(http.get).mockRejectedValue(new Error('Network Error'))

    await expect(versionApi.getProductVersion()).rejects.toThrow('Network Error')
  })
})

describe('apiRootFrom', () => {
  it.each([
    ['/api', ''],
    ['/api/', ''],
    ['http://localhost:5000/api', 'http://localhost:5000'],
    ['/taskdeck/api', '/taskdeck'],
    ['', ''],
  ])('derives %o -> %o', (apiBase, expected) => {
    expect(apiRootFrom(apiBase)).toBe(expected)
  })
})

describe('resolveApiRoot', () => {
  it('applies the derivation to the base this build carries', () => {
    expect(resolveApiRoot()).toBe(EXPECTED_API_ROOT)
  })
})
