import { describe, expect, it } from 'vitest'
import { isLocaleCatalogRequest, isStaticAssetRequest } from '../../pwa/runtimeCachePolicy'

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

  it('still caches the assets the build emits', () => {
    expect(isStaticAssetRequest(request('https://taskdeck.example/assets/avatar-a1b2.png'))).toBe(true)
    expect(isStaticAssetRequest(request('https://taskdeck.example/icons/icon-192x192.png'))).toBe(true)
  })
})
