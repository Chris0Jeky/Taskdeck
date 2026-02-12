import { describe, expect, it } from 'vitest'
import { sanitizeInternalRedirect } from '../../utils/navigation'

describe('navigation utils', () => {
  it('allows internal absolute paths', () => {
    const redirect = sanitizeInternalRedirect('/workspace/boards/123?tab=activity')
    expect(redirect).toBe('/workspace/boards/123?tab=activity')
  })

  it('rejects absolute external URLs', () => {
    const redirect = sanitizeInternalRedirect('https://example.com')
    expect(redirect).toBe('/workspace/boards')
  })

  it('rejects protocol-relative redirects', () => {
    const redirect = sanitizeInternalRedirect('//example.com')
    expect(redirect).toBe('/workspace/boards')
  })

  it('uses fallback for empty values', () => {
    const redirect = sanitizeInternalRedirect('')
    expect(redirect).toBe('/workspace/boards')
  })
})
