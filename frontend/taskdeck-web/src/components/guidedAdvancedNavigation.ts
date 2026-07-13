export const GUIDED_ADVANCED_DESTINATION_ORDER = [
  'agents',
  'metrics',
  'cohorts',
  'integrations',
  'ops',
  'ops-endpoints',
  'ops-logs',
  'api-keys',
  'dev-tools',
] as const

export function orderGuidedAdvancedDestinations<T extends { id: string }>(items: readonly T[]): T[] {
  const byId = new Map(items.map(item => [item.id, item]))
  return GUIDED_ADVANCED_DESTINATION_ORDER.flatMap(id => {
    const item = byId.get(id)
    return item ? [item] : []
  })
}
