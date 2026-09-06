import { describe, expect, it } from 'vitest'
import { resolveHubUrl } from '../../composables/useBoardRealtime'
import { apiRootFrom } from '../../utils/apiRoot'

describe('apiRootFrom', () => {
  it.each([
    ['', ''],
    ['/', '/'],
    ['/api', ''],
    ['/api/', ''],
    ['/API/', ''],
    ['http://localhost:5000/api', 'http://localhost:5000'],
    ['https://example.test/taskdeck/api/', 'https://example.test/taskdeck'],
    ['/taskdeck/api', '/taskdeck'],
    ['/taskdeck/api/', '/taskdeck'],
    ['/taskdeck/apiary', '/taskdeck/apiary'],
    ['/taskdeck/api/cards', '/taskdeck/api/cards'],
    ['https://example.test/api/api', 'https://example.test/api'],
    ['api', 'api'],
  ])('normalizes %o to %o', (apiBase, expected) => {
    expect(apiRootFrom(apiBase)).toBe(expected)
  })

  it('keeps the realtime hub suffix outside the shared root utility', () => {
    expect(resolveHubUrl()).toBe('http://localhost:5000/hubs/boards')
  })
})
