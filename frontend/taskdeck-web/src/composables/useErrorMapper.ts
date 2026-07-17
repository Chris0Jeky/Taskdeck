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
  return error.message || ERROR_MESSAGES[error.errorCode] || ERROR_MESSAGES.UnexpectedError || 'An unexpected error occurred.'
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

/**
 * True when the error is a backend 400 ValidationError — BOTH the status and
 * the errorCode must match, so an unrelated 400 (or a ValidationError code on
 * another status) is never classified as the review-gate verdict. The
 * automation `/diff` endpoint returns this (PR #1395 / #1376) when it runs
 * Apply's gates at diff time — "Proposal has expired" or "Proposal must
 * contain at least one operation". Review surfaces render the backend's ACTUAL
 * message as an explicit invalid presentation instead of tearing down the pane
 * + toasting (#1397).
 */
export function isValidationError(err: unknown): boolean {
  if (typeof err !== 'object' || err === null) return false
  const candidate = err as { response?: { status?: number; data?: { errorCode?: string } } }
  return (
    candidate.response?.status === 400 &&
    candidate.response?.data?.errorCode === 'ValidationError'
  )
}

/**
 * The backend-provided reason for an invalid-preview 400 ValidationError,
 * trimmed — or `null` when the backend sent no message (or a whitespace-only
 * one). Unlike `getErrorDisplay`, this deliberately does NOT substitute the
 * generic "Please check your input" ValidationError copy: the invalid-diff
 * presentation renders its own specific fallback ("This proposal contains no
 * operations…") when the reason is absent, and a masking generic string would
 * suppress it (#1397 / #1414 review).
 */
export function getValidationReason(err: unknown): string | null {
  const apiError = parseApiError(err)
  const message = apiError?.message?.trim()
  return message && message.length > 0 ? message : null
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
