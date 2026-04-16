/**
 * Unit tests for the retry helpers in api/httpRetry.ts (issue #854 / FE-15).
 *
 * These tests exercise the pure helpers directly so we can assert delay
 * shapes and edge cases without going through the Axios pipeline.
 */
import { describe, it, expect } from 'vitest'
import type { AxiosError } from 'axios'
import axios from 'axios'
import {
  BASE_DELAY_MS,
  MAX_DELAY_MS,
  MAX_RETRIES,
  computeBackoff,
  computeRetryDelay,
  isIdempotent,
  isRetryableError,
  parseRetryAfter,
} from '../../api/httpRetry'

function makeError(overrides: Partial<AxiosError> & { status?: number; headers?: Record<string, string>; method?: string }): AxiosError {
  const { status, headers, method, ...rest } = overrides
  return {
    isAxiosError: true,
    name: 'AxiosError',
    message: 'test',
    config: { method: method ?? 'get', url: '/x' },
    response: status !== undefined
      ? { status, statusText: '', data: {}, headers: headers ?? {}, config: {} as never }
      : undefined,
    ...rest,
  } as AxiosError
}

describe('httpRetry — isIdempotent', () => {
  it('treats GET / HEAD / OPTIONS / PUT / DELETE as idempotent', () => {
    expect(isIdempotent('GET')).toBe(true)
    expect(isIdempotent('HEAD')).toBe(true)
    expect(isIdempotent('OPTIONS')).toBe(true)
    expect(isIdempotent('PUT')).toBe(true)
    expect(isIdempotent('DELETE')).toBe(true)
  })

  it('is case-insensitive', () => {
    expect(isIdempotent('get')).toBe(true)
    expect(isIdempotent('Put')).toBe(true)
    expect(isIdempotent('dElEtE')).toBe(true)
  })

  it('treats POST / PATCH as non-idempotent', () => {
    expect(isIdempotent('POST')).toBe(false)
    expect(isIdempotent('post')).toBe(false)
    expect(isIdempotent('PATCH')).toBe(false)
  })

  it('returns false for undefined / empty method', () => {
    expect(isIdempotent(undefined)).toBe(false)
    expect(isIdempotent('')).toBe(false)
  })
})

describe('httpRetry — parseRetryAfter', () => {
  it('returns null for null / undefined / empty input', () => {
    expect(parseRetryAfter(null)).toBeNull()
    expect(parseRetryAfter(undefined)).toBeNull()
    expect(parseRetryAfter('')).toBeNull()
    expect(parseRetryAfter('   ')).toBeNull()
  })

  it('parses numeric seconds into milliseconds', () => {
    expect(parseRetryAfter('0')).toBe(0)
    expect(parseRetryAfter('2')).toBe(2000)
    expect(parseRetryAfter('30')).toBe(30_000)
  })

  it('caps numeric seconds at MAX_DELAY_MS', () => {
    expect(parseRetryAfter('9999999')).toBe(MAX_DELAY_MS)
  })

  it('rejects negative numeric values', () => {
    expect(parseRetryAfter('-5')).toBeNull()
  })

  it('parses RFC 1123 HTTP-date relative to now', () => {
    const now = Date.parse('2026-04-16T12:00:00Z')
    const target = new Date(now + 5000).toUTCString() // 5 seconds in the future
    const parsed = parseRetryAfter(target, now)
    expect(parsed).toBe(5000)
  })

  it('clamps past HTTP-date to 0', () => {
    const now = Date.parse('2026-04-16T12:00:00Z')
    const past = new Date(now - 10_000).toUTCString()
    expect(parseRetryAfter(past, now)).toBe(0)
  })

  it('caps future HTTP-date at MAX_DELAY_MS', () => {
    const now = Date.parse('2026-04-16T12:00:00Z')
    const farFuture = new Date(now + 10 * 60 * 1000).toUTCString() // 10 min
    expect(parseRetryAfter(farFuture, now)).toBe(MAX_DELAY_MS)
  })

  it('returns null for malformed input', () => {
    expect(parseRetryAfter('not-a-date')).toBeNull()
    expect(parseRetryAfter('abc123')).toBeNull()
  })
})

describe('httpRetry — computeBackoff', () => {
  it('doubles per attempt at the midpoint (random=0.5 → no jitter)', () => {
    const r = () => 0.5
    expect(computeBackoff(1, r)).toBe(BASE_DELAY_MS)
    expect(computeBackoff(2, r)).toBe(BASE_DELAY_MS * 2)
    expect(computeBackoff(3, r)).toBe(BASE_DELAY_MS * 4)
  })

  it('applies at most +/-25% jitter', () => {
    const delays = [0, 0.25, 0.5, 0.75, 1].map((v) => computeBackoff(2, () => v))
    const base = BASE_DELAY_MS * 2
    for (const d of delays) {
      expect(d).toBeGreaterThanOrEqual(Math.floor(base * 0.75))
      expect(d).toBeLessThanOrEqual(Math.ceil(base * 1.25))
    }
  })

  it('caps at MAX_DELAY_MS even for very high attempts', () => {
    expect(computeBackoff(100, () => 0.5)).toBeLessThanOrEqual(MAX_DELAY_MS)
  })
})

describe('httpRetry — isRetryableError', () => {
  it('retries GET 500/502/503/504', () => {
    for (const s of [500, 502, 503, 504]) {
      expect(isRetryableError(makeError({ status: s, method: 'get' }))).toBe(true)
    }
  })

  it('retries GET 429', () => {
    expect(isRetryableError(makeError({ status: 429, method: 'get' }))).toBe(true)
  })

  it('does not retry 4xx (401/403/404/409/422)', () => {
    for (const s of [400, 401, 403, 404, 409, 422]) {
      expect(isRetryableError(makeError({ status: s, method: 'get' }))).toBe(false)
    }
  })

  it('does not retry POST even on 500', () => {
    expect(isRetryableError(makeError({ status: 500, method: 'post' }))).toBe(false)
  })

  it('does not retry PATCH even on 500', () => {
    expect(isRetryableError(makeError({ status: 500, method: 'patch' }))).toBe(false)
  })

  it('retries GET network error (no response)', () => {
    expect(isRetryableError(makeError({ method: 'get' }))).toBe(true)
  })

  it('does not retry a cancelled request', () => {
    const cancelled = new axios.Cancel('user aborted') as unknown as AxiosError
    expect(isRetryableError(cancelled)).toBe(false)
  })
})

describe('httpRetry — computeRetryDelay', () => {
  it('honours Retry-After on 429 (seconds)', () => {
    const err = makeError({ status: 429, headers: { 'retry-after': '3' }, method: 'get' })
    expect(computeRetryDelay(err, 1, { random: () => 0.5 })).toBe(3000)
  })

  it('honours Retry-After on 429 (HTTP-date)', () => {
    const now = Date.parse('2026-04-16T12:00:00Z')
    const target = new Date(now + 4000).toUTCString()
    const err = makeError({ status: 429, headers: { 'retry-after': target }, method: 'get' })
    expect(computeRetryDelay(err, 1, { now, random: () => 0.5 })).toBe(4000)
  })

  it('falls back to exponential backoff on malformed Retry-After', () => {
    const err = makeError({ status: 429, headers: { 'retry-after': 'garbage' }, method: 'get' })
    expect(computeRetryDelay(err, 1, { random: () => 0.5 })).toBe(BASE_DELAY_MS)
  })

  it('uses exponential backoff on 5xx (no Retry-After)', () => {
    const err = makeError({ status: 500, method: 'get' })
    expect(computeRetryDelay(err, 2, { random: () => 0.5 })).toBe(BASE_DELAY_MS * 2)
  })
})

describe('httpRetry — exported constants', () => {
  it('defaults to 3 retries', () => {
    expect(MAX_RETRIES).toBe(3)
  })

  it('base delay is 1s', () => {
    expect(BASE_DELAY_MS).toBe(1000)
  })

  it('max delay cap is 60s', () => {
    expect(MAX_DELAY_MS).toBe(60_000)
  })
})
