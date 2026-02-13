/**
 * Extracts a human-readable error message from an unknown error value.
 * Checks for Axios-style response errors first, then the standard Error.message,
 * falling back to the provided default string.
 */
export function getErrorMessage(err: unknown, fallback: string): string {
  if (typeof err !== 'object' || err === null) {
    return fallback
  }

  const typed = err as { response?: { data?: { message?: unknown } }; message?: unknown }

  const responseMessage = typed.response?.data?.message
  if (typeof responseMessage === 'string' && responseMessage.trim().length > 0) {
    return responseMessage
  }

  if (typeof typed.message === 'string' && typed.message.trim().length > 0) {
    return typed.message
  }

  return fallback
}
