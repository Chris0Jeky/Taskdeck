import { describe, expect, it } from 'vitest'
import {
  createLocaleCatalogRuntimePattern,
  createStaticAssetRuntimePattern,
  isLocaleCatalogRequest,
  isStaticAssetRequest,
} from '../../pwa/runtimeCachePolicy'

function request(url: string) {
  return { url: new URL(url) }
}

describe('PWA runtime cache policy', () => {
  it('preserves locale and static asset caching outside the API surface', () => {
    expect(isLocaleCatalogRequest(request('https://taskdeck.example/assets/it-abc.js'))).toBe(true)
    expect(isStaticAssetRequest(request('https://cdn.example/assets/avatar.png'))).toBe(true)
  })

  it.each([
    'https://taskdeck.example/api/assets/it-abc.js',
    'https://taskdeck.example/api/avatar.png',
    'https://cdn.example/api/assets/es-abc.js',
    'https://cdn.example/api/avatar.png',
    'https://taskdeck.example/API/avatar.png',
    'https://taskdeck.example/%61pi/avatar.png',
    'https://taskdeck.example/api%2Favatar.png',
    'https://taskdeck.example//api/avatar.png',
    'https://cdn.example/%2Fapi/avatar.png',
  ])('never admits an API path to retained runtime caches: %s', (url) => {
    expect(isLocaleCatalogRequest(request(url))).toBe(false)
    expect(isStaticAssetRequest(request(url))).toBe(false)
  })

  it.each([
    // A prefixed VITE_API_BASE_URL is a supported deployment shape, and a username
    // may end in an image extension, so extension matching alone is not a boundary.
    'https://taskdeck.example/taskdeck/api/users/by-username/alice.png',
    'https://taskdeck.example/deploy/api/boards/1/cover.svg',
    'https://taskdeck.example/uploads/alice.png',
    'https://taskdeck.example/alice.png',
  ])('admits only build-owned directories, whatever the API base: %s', (url) => {
    expect(isStaticAssetRequest(request(url))).toBe(false)
  })

  it.each([
    'https://taskdeck.example/assets/api/users/by-username/alice.png',
    'https://taskdeck.example/assets/api/boards/1/cover.svg',
    'https://taskdeck.example/assets/%61pi/users/by-username/alice.png',
  ])('rejects responses under a configured asset API base: %s', (url) => {
    expect(isLocaleCatalogRequest(request(url), '/assets/api')).toBe(false)
    expect(isStaticAssetRequest(request(url), '/assets/api')).toBe(false)
  })

  it.each([
    'https://taskdeck.example/icons/api/users/by-username/alice.png',
    'https://taskdeck.example/icons/api/boards/1/cover.svg',
  ])('rejects responses under a configured icon API base: %s', (url) => {
    expect(isLocaleCatalogRequest(request(url), '/icons/api')).toBe(false)
    expect(isStaticAssetRequest(request(url), '/icons/api')).toBe(false)
  })

  it('fails closed when the configured API base is malformed or ambiguous', () => {
    const url = request('https://taskdeck.example/assets/avatar.png')

    expect(isLocaleCatalogRequest(url, 'assets/api')).toBe(false)
    expect(isStaticAssetRequest(url, 'assets/api')).toBe(false)
    expect(isLocaleCatalogRequest(url, '/assets/api?tenant=one')).toBe(false)
    expect(isStaticAssetRequest(url, '/assets/api?tenant=one')).toBe(false)
    expect(isLocaleCatalogRequest(url, '/äpi')).toBe(false)
    expect(isStaticAssetRequest(url, '/äpi')).toBe(false)
  })

  it('builds match-nothing worker patterns for malformed API bases', () => {
    const staticPattern = createStaticAssetRuntimePattern('assets/api')
    const localePattern = createLocaleCatalogRuntimePattern('/assets/api?tenant=one')

    expect(staticPattern.test('https://taskdeck.example/assets/avatar.png')).toBe(false)
    expect(localePattern.test('https://taskdeck.example/assets/it-a.js')).toBe(false)
  })

  it('still caches the assets the build emits', () => {
    expect(isStaticAssetRequest(request('https://taskdeck.example/assets/avatar-a1b2.png'))).toBe(true)
    expect(isStaticAssetRequest(request('https://taskdeck.example/icons/icon-192x192.png'))).toBe(true)
  })
})
