import { expect } from '@playwright/test'

export type PollingOptions<T> = {
  timeoutMs?: number
  intervalMs?: number
  description?: string
  /**
   * Return a non-empty string to abort early with that message.
   */
  abortIf?: (value: T) => string | undefined
}

function serializeForDiagnostics(value: unknown): string {
  if (value === undefined) {
    return 'undefined'
  }

  if (value === null) {
    return 'null'
  }

  if (typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean') {
    return `${value}`
  }

  try {
    return JSON.stringify(value)
  } catch {
    return String(value)
  }
}

export async function pollUntil<T>(
  poller: () => Promise<T>,
  predicate: (value: T) => boolean,
  options: PollingOptions<T> = {},
): Promise<T> {
  const timeoutMs = options.timeoutMs ?? 20000
  const intervalMs = options.intervalMs ?? 500
  let lastValue: T | undefined
  try {
    await expect.poll(async () => {
      const value = await poller()
      lastValue = value
      const abortMessage = options.abortIf?.(value)
      if (abortMessage) {
        throw new Error(abortMessage)
      }
      return predicate(value)
    }, {
      timeout: timeoutMs,
      intervals: [intervalMs],
      message: options.description,
    }).toBeTruthy()
  } catch (error) {
    const desc = options.description ?? 'Polling condition'
    const diag = lastValue === undefined ? 'no values observed' : `last value: ${serializeForDiagnostics(lastValue)}`
    const wrapped = new Error(`${desc} failed after ${timeoutMs}ms (${diag}). ${(error as Error).message}`)
    wrapped.stack = (error as Error).stack
    throw wrapped
  }

  if (lastValue === undefined) {
    throw new Error(`${options.description ?? 'Polling condition'} succeeded without producing a value`)
  }

  return lastValue
}
