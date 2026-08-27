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

/**
 * True when the error is a backend 403 or 404 for a proposal read — the signals
 * `AuthorizeProposalAsync(requireWriteAccess:false)` returns when the caller no
 * longer has board access (403) or the proposal/board/requester is gone (404;
 * the backend 404s a revoked read to avoid leaking existence). Review surfaces
 * use this to RETRACT a stored preview whose access was revoked mid-session,
 * while ignoring transient errors (5xx/network) that must NOT tear down an
 * otherwise-inspectable local preview (#1414 P2: re-check access on reveal).
 */
export function isAccessDeniedError(err: unknown): boolean {
  if (typeof err !== 'object' || err === null) return false
  const candidate = err as { response?: { status?: number } }
  const status = candidate.response?.status
  return status === 403 || status === 404
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

/**
 * Read the client-generated correlation id (`X-Request-Id`, stamped by
 * `api/http.ts`) off a failed request's config. `error.config.headers` is an
 * `AxiosHeaders` instance in production — read it through its case-insensitive
 * `get()` — and a plain object in tests/other shapes — matched case-insensitively.
 * Never read the RESPONSE header: CORS does not expose it to the browser.
 */
function readRequestId(config: unknown): string | null {
  if (typeof config !== 'object' || config === null) return null
  const headers = (config as { headers?: unknown }).headers
  if (typeof headers !== 'object' || headers === null) return null

  const maybeGet = (headers as { get?: unknown }).get
  if (typeof maybeGet === 'function') {
    const value = (headers as { get: (name: string) => unknown }).get('X-Request-Id')
    if (typeof value === 'string' && value.trim().length > 0) return value.trim()
  }

  for (const [key, value] of Object.entries(headers as Record<string, unknown>)) {
    if (key.toLowerCase() === 'x-request-id' && typeof value === 'string' && value.trim().length > 0) {
      return value.trim()
    }
  }
  return null
}

/**
 * A multi-line, copy-pasteable diagnostic for a failed HTTP request — the
 * inspectable receipt that makes an opaque "request failed" toast actionable
 * (GH-1938). Assembled from whatever the error carries: HTTP status, the
 * endpoint (method + URL from `error.config`), the backend `errorCode`, and the
 * client correlation id (`X-Request-Id`). A network failure with no response
 * still yields the endpoint and request id.
 *
 * Returns `null` when the error is not an axios-shaped request failure (e.g. a
 * bare `Error`), so callers render no diagnostic block rather than an empty one.
 */
export function getErrorDetails(err: unknown): string | null {
  if (typeof err !== 'object' || err === null) return null
  const candidate = err as {
    response?: { status?: number; data?: { errorCode?: string } }
    config?: { method?: string; url?: string }
  }

  const lines: string[] = []

  const status = candidate.response?.status
  if (typeof status === 'number') {
    lines.push(`Status: ${status}`)
  }

  const method = candidate.config?.method
  const url = candidate.config?.url
  if (typeof url === 'string' && url.length > 0) {
    lines.push(
      typeof method === 'string' && method.length > 0
        ? `Endpoint: ${method.toUpperCase()} ${url}`
        : `Endpoint: ${url}`,
    )
  }

  const errorCode = candidate.response?.data?.errorCode
  if (typeof errorCode === 'string' && errorCode.trim().length > 0) {
    lines.push(`Code: ${errorCode.trim()}`)
  }

  const requestId = readRequestId(candidate.config)
  if (requestId) {
    lines.push(`Request ID: ${requestId}`)
  }

  return lines.length > 0 ? lines.join('\n') : null
}
