import { describe, expect, it } from 'vitest'
import { getErrorMessage } from '../../utils/errorMessage'

describe('errorMessage', () => {
  it('prefers API response message when present', () => {
    const error = { response: { data: { message: 'Backend validation failed' } }, message: 'Generic error' }

    expect(getErrorMessage(error, 'Fallback')).toBe('Backend validation failed')
  })

  it('falls back to Error.message when response message is missing', () => {
    const error = { message: 'Network unavailable' }

    expect(getErrorMessage(error, 'Fallback')).toBe('Network unavailable')
  })

  it('returns fallback when error has no usable message', () => {
    expect(getErrorMessage({ response: { data: { message: '   ' } } }, 'Fallback')).toBe('Fallback')
    expect(getErrorMessage('plain text error', 'Fallback')).toBe('Fallback')
  })
})
