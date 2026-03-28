import { describe, expect, it } from 'vitest'
import { getErrorDisplay, mapErrorToMessage, parseApiError } from '../../composables/useErrorMapper'

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
})
