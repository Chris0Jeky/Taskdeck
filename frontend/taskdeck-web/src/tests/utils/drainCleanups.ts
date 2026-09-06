export function drainCleanups(cleanups: Array<() => void>): void {
  const errors: unknown[] = []

  while (cleanups.length > 0) {
    const cleanup = cleanups.pop()!
    try {
      cleanup()
    } catch (error: unknown) {
      errors.push(error)
    }
  }

  if (errors.length === 1) throw errors[0]
  if (errors.length > 1) {
    throw new AggregateError(errors, 'One or more cleanup callbacks failed')
  }
}
