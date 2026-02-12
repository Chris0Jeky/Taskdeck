import type { ApiError } from '../types/api'

const ERROR_MESSAGES: Record<string, string> = {
  ValidationError: 'Please check your input and try again.',
  NotFound: 'The requested resource was not found.',
  AuthenticationFailed: 'Authentication failed. Please log in again.',
  Forbidden: 'You do not have permission to perform this action.',
  Conflict: 'A conflict occurred. Please refresh and try again.',
  WipLimitExceeded: 'Work-in-progress limit would be exceeded.',
  UnexpectedError: 'An unexpected error occurred.',
}

export function mapErrorToMessage(error: ApiError): string {
  return error.message || ERROR_MESSAGES[error.errorCode] || ERROR_MESSAGES.UnexpectedError
}

export function parseApiError(err: unknown): ApiError | null {
  if (typeof err !== 'object' || err === null) return null
  const typed = err as { response?: { data?: { errorCode?: string; message?: string } } }
  if (typed.response?.data?.errorCode) {
    return {
      errorCode: typed.response.data.errorCode,
      message: typed.response.data.message ?? '',
    }
  }
  return null
}

export function getErrorDisplay(err: unknown, fallback: string): { message: string; code: string | null } {
  const apiError = parseApiError(err)
  if (apiError) {
    return {
      message: mapErrorToMessage(apiError),
      code: apiError.errorCode,
    }
  }
  if (typeof err === 'object' && err !== null) {
    const typed = err as { message?: unknown }
    if (typeof typed.message === 'string' && typed.message.trim().length > 0) {
      return { message: typed.message, code: null }
    }
  }
  return { message: fallback, code: null }
}
