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
})
