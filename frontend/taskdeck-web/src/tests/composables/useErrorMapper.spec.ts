import { describe, expect, it } from 'vitest'
import {
  getErrorDetails,
  getErrorDisplay,
  getValidationReason,
  isValidationError,
  mapErrorToMessage,
  parseApiError,
} from '../../composables/useErrorMapper'

describe('useErrorMapper', () => {
  describe('mapErrorToMessage', () => {
    it('maps ValidationError to the expected user-friendly message', () => {
      expect(mapErrorToMessage({ errorCode: 'ValidationError', message: '' }))
        .toBe('Please check your input and try again.')
    })

    it('maps the remaining known error codes to user-friendly messages', () => {
      expect(mapErrorToMessage({ errorCode: 'NotFound', message: '' }))
        .toBe('The requested resource was not found.')
      expect(mapErrorToMessage({ errorCode: 'AuthenticationFailed', message: '' }))
        .toBe('Authentication failed. Please log in again.')
      expect(mapErrorToMessage({ errorCode: 'Forbidden', message: '' }))
        .toBe('You do not have permission to perform this action.')
      expect(mapErrorToMessage({ errorCode: 'Conflict', message: '' }))
        .toBe('A conflict occurred. Please refresh and try again.')
      expect(mapErrorToMessage({ errorCode: 'WipLimitExceeded', message: '' }))
        .toBe('Work-in-progress limit would be exceeded.')
    })

    it('returns the explicit error message before falling back to the code lookup', () => {
      expect(mapErrorToMessage({
        errorCode: 'ValidationError',
        message: 'Server provided a better explanation.',
      })).toBe('Server provided a better explanation.')
    })

    it('falls back to the unexpected error message for unknown codes', () => {
      expect(mapErrorToMessage({ errorCode: 'CustomUnknownCode', message: '' }))
        .toBe('An unexpected error occurred.')
    })
  })

  describe('parseApiError', () => {
    it('extracts errorCode and message from the API error shape', () => {
      expect(parseApiError({
        response: {
          data: {
            errorCode: 'Conflict',
            message: 'A more specific conflict reason.',
          },
        },
      })).toEqual({
        errorCode: 'Conflict',
        message: 'A more specific conflict reason.',
      })
    })

    it('returns null for non-object inputs', () => {
      expect(parseApiError(null)).toBeNull()
      expect(parseApiError(undefined)).toBeNull()
      expect(parseApiError('bad')).toBeNull()
      expect(parseApiError(42)).toBeNull()
    })

    it('returns null when errorCode is missing from response data', () => {
      expect(parseApiError({
        response: {
          data: {
            message: 'Missing code',
          },
        },
      })).toBeNull()
    })
  })

  describe('isValidationError', () => {
    // #1397 MEDIUM-1: the classifier requires status 400 AND the ValidationError
    // code — neither alone may classify as the review-gate verdict.
    it('is true only when status is 400 AND errorCode is ValidationError', () => {
      expect(isValidationError({
        response: { status: 400, data: { errorCode: 'ValidationError', message: 'Proposal has expired' } },
      })).toBe(true)
    })

    it('is false for a 400 without the ValidationError code', () => {
      expect(isValidationError({
        response: { status: 400, data: { errorCode: 'Conflict', message: 'nope' } },
      })).toBe(false)
      expect(isValidationError({ response: { status: 400, data: {} } })).toBe(false)
      expect(isValidationError({ response: { status: 400 } })).toBe(false)
    })

    it('is false for a ValidationError code on another status', () => {
      expect(isValidationError({
        response: { status: 500, data: { errorCode: 'ValidationError', message: 'boom' } },
      })).toBe(false)
    })

    it('is false for non-object and shapeless inputs', () => {
      expect(isValidationError(null)).toBe(false)
      expect(isValidationError(undefined)).toBe(false)
      expect(isValidationError('bad')).toBe(false)
      expect(isValidationError(new Error('boom'))).toBe(false)
    })
  })

  describe('getErrorDisplay', () => {
    it('returns the mapped API error message and code for parseable API errors', () => {
      expect(getErrorDisplay({
        response: {
          data: {
            errorCode: 'Forbidden',
            message: '',
          },
        },
      }, 'Fallback message')).toEqual({
        message: 'You do not have permission to perform this action.',
        code: 'Forbidden',
      })
    })

    it('falls back to err.message when the input is not an API error shape', () => {
      expect(getErrorDisplay(new Error('Local runtime failure'), 'Fallback message')).toEqual({
        message: 'Local runtime failure',
        code: null,
      })
    })

    it('returns the fallback string when no usable message exists', () => {
      expect(getErrorDisplay({ message: '   ' }, 'Fallback message')).toEqual({
        message: 'Fallback message',
        code: null,
      })
    })
  })

  describe('getValidationReason', () => {
    // #1397 / #1414 review: the invalid-diff presentation renders its own
    // specific "no operations" fallback when the backend gave no reason. This
    // helper must therefore treat a blank message as absent (return null) and
    // must NOT substitute the generic ValidationError copy that would mask it.
    it('returns the trimmed backend message when one is present', () => {
      expect(getValidationReason({
        response: { status: 400, data: { errorCode: 'ValidationError', message: '  Proposal has expired  ' } },
      })).toBe('Proposal has expired')
    })

    it('returns null for an empty backend message', () => {
      expect(getValidationReason({
        response: { status: 400, data: { errorCode: 'ValidationError', message: '' } },
      })).toBeNull()
    })

    it('returns null for a whitespace-only backend message', () => {
      expect(getValidationReason({
        response: { status: 400, data: { errorCode: 'ValidationError', message: '   ' } },
      })).toBeNull()
    })

    it('returns null when the input is not an API error shape', () => {
      expect(getValidationReason(new Error('Local runtime failure'))).toBeNull()
      expect(getValidationReason(null)).toBeNull()
    })
  })

  describe('getErrorDetails', () => {
    // GH-1938: the diagnostic receipt that makes an opaque capture-failure toast
    // inspectable. Status + endpoint + errorCode + client correlation id.
    it('assembles status, endpoint, code, and request id from an axios error', () => {
      const details = getErrorDetails({
        response: { status: 503, data: { errorCode: 'UnexpectedError' } },
        config: {
          method: 'post',
          url: '/capture/items',
          headers: { 'X-Request-Id': 'req-abc-123' },
        },
      })
      expect(details).not.toBeNull()
      expect(details).toContain('Status: 503')
      expect(details).toContain('Endpoint: POST /capture/items')
      expect(details).toContain('Code: UnexpectedError')
      expect(details).toContain('Request ID: req-abc-123')
    })

    it('reads the request id from an AxiosHeaders-like case-insensitive get()', () => {
      const headers = {
        get: (name: string) => (name.toLowerCase() === 'x-request-id' ? 'req-from-get' : null),
      }
      const details = getErrorDetails({
        response: { status: 500 },
        config: { method: 'post', url: '/capture/items', headers },
      })
      expect(details).toContain('Request ID: req-from-get')
    })

    it('still yields the endpoint and request id for a network failure with no response', () => {
      const details = getErrorDetails({
        config: {
          method: 'get',
          url: '/workspace/home',
          headers: { 'x-request-id': 'req-network' },
        },
      })
      expect(details).not.toBeNull()
      expect(details).not.toContain('Status:')
      expect(details).toContain('Endpoint: GET /workspace/home')
      expect(details).toContain('Request ID: req-network')
    })

    it('returns null for a bare Error with no request context', () => {
      expect(getErrorDetails(new Error('offline'))).toBeNull()
    })

    it('returns null for non-object inputs', () => {
      expect(getErrorDetails(null)).toBeNull()
      expect(getErrorDetails(undefined)).toBeNull()
      expect(getErrorDetails('boom')).toBeNull()
    })
  })
})
