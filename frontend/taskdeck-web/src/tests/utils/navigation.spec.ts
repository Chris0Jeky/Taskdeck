import { describe, expect, it } from 'vitest'
import {
  isAuthRoutePath,
  normalizeBoardIdQueryParam,
  normalizePathname,
  sanitizeInternalRedirect,
} from '../../utils/navigation'

describe('navigation utils', () => {
  it('allows internal absolute paths', () => {
    const redirect = sanitizeInternalRedirect('/workspace/boards/123?tab=activity')
    expect(redirect).toBe('/workspace/boards/123?tab=activity')
  })

  it('rejects absolute external URLs', () => {
    const redirect = sanitizeInternalRedirect('https://example.com')
    expect(redirect).toBe('/workspace/home')
  })

  it('rejects protocol-relative redirects', () => {
    const redirect = sanitizeInternalRedirect('//example.com')
    expect(redirect).toBe('/workspace/home')
  })

  it('uses fallback for empty values', () => {
    const redirect = sanitizeInternalRedirect('')
    expect(redirect).toBe('/workspace/home')
  })

  it('normalizes trailing slashes for paths', () => {
    expect(normalizePathname('/login/')).toBe('/login')
    expect(normalizePathname('/register///')).toBe('/register')
    expect(normalizePathname('/')).toBe('/')
  })

  it('detects auth routes by pathname only', () => {
    expect(isAuthRoutePath('/login')).toBe(true)
    expect(isAuthRoutePath('/register')).toBe(true)
    expect(isAuthRoutePath('/login/')).toBe(true)
    expect(isAuthRoutePath('/workspace/boards')).toBe(false)
  })

  it('normalizes board id query params from strings or arrays', () => {
    expect(normalizeBoardIdQueryParam(' board-7 ')).toBe('board-7')
    expect(normalizeBoardIdQueryParam([' board-8 ', 'board-9'])).toBe('board-8')
    expect(normalizeBoardIdQueryParam(undefined)).toBe('')
  })
})
