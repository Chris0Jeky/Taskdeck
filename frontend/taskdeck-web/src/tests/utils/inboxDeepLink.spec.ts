import { describe, expect, it } from 'vitest'
import { getCaptureIdFromHash, isHttpNotFound } from '../../utils/inboxDeepLink'

describe('inbox deep-link utilities', () => {
  describe('getCaptureIdFromHash', () => {
    it('extracts a capture id from the canonical hash', () => {
      expect(getCaptureIdFromHash('#capture-capture-123')).toBe('capture-123')
    })

    it('trims outer whitespace before decoding the capture id', () => {
      expect(getCaptureIdFromHash('#capture-%63apture%2F123')).toBe('capture/123')
    })

    it.each(['', '#other-123', '#capture-', '#capture-   '])(
      'returns null for an invalid hash: %s',
      (hash) => {
        expect(getCaptureIdFromHash(hash)).toBeNull()
      },
    )

    it('returns null when the capture id is not valid URI encoding', () => {
      expect(getCaptureIdFromHash('#capture-%')).toBeNull()
    })
  })

  describe('isHttpNotFound', () => {
    it.each([
      { response: { status: 404 } },
      { response: { data: { errorCode: 'NotFound' } } },
    ])('recognizes a not-found response: %o', (error) => {
      expect(isHttpNotFound(error)).toBe(true)
    })

    it.each([
      { response: { status: 400 } },
      { response: { data: { errorCode: 'Forbidden' } } },
      null,
      'not an error object',
    ])('rejects non-not-found responses: %o', (error) => {
      expect(isHttpNotFound(error)).toBe(false)
    })
  })
})
