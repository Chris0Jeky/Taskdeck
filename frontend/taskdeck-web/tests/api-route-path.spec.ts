import { describe, expect, it } from 'vitest'

import { apiRoutePath } from './e2e/support/apiRoutePath'

describe('E2E API route path resolution', () => {
  it.each([
    ['default API base', 'http://localhost:5000/api', '/api/boards/board-123'],
    ['prefixed API base', 'http://localhost:5000/taskdeck/api', '/taskdeck/api/boards/board-123'],
    ['trailing slash', 'http://localhost:5000/taskdeck/api/', '/taskdeck/api/boards/board-123'],
  ])('keeps the configured path for the %s', (_label, apiBaseUrl, expectedPath) => {
    expect(apiRoutePath(apiBaseUrl, 'boards/board-123')).toBe(expectedPath)
  })
})
