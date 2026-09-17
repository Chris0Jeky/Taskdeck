function requirePositiveInteger(value, name) {
  if (!Number.isInteger(value) || value < 1) {
    throw new Error(`${name} must be a positive integer.`)
  }
}

/**
 * Keep each VU on its own board while allowing a smaller authenticated account
 * pool. The k6 profile measures board API throughput; the separate Playwright
 * concurrency suite owns stale-write conflict behavior.
 */
export function buildActorAssignments(vus, userPool) {
  requirePositiveInteger(vus, 'vus')
  requirePositiveInteger(userPool, 'userPool')

  const accountCount = Math.min(vus, userPool)

  return Array.from({ length: vus }, (_, vuIndex) => ({
    vuIndex: vuIndex + 1,
    accountIndex: vuIndex % accountCount,
    boardIndex: vuIndex,
  }))
}
