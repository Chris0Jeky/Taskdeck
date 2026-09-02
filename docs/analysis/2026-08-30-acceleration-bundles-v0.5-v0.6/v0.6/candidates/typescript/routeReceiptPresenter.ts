
export type RouteAlternative = {
  processorId: string
  executionMode: string
  eligibility: 'eligible' | 'ineligible' | 'eligible-not-chosen' | 'chosen'
  reasonCodes: string[]
  estimatedCost?: number | null
  currency?: string | null
}

export type RouteReceipt = {
  capability: string
  policyDigest: string
  chosenProcessorId?: string | null
  cacheHit: boolean
  forcedRerun: boolean
  alternatives: RouteAlternative[]
}

export type RouteReceiptView = {
  headline: string
  chosen?: string
  cacheLabel?: string
  reasons: Array<{ processorId: string; reasons: string[] }>
}

export function presentRouteReceipt(receipt: RouteReceipt): RouteReceiptView {
  const chosen = receipt.alternatives.find((item) => item.eligibility === 'chosen')
  const reasons = receipt.alternatives
    .filter((item) => item.eligibility === 'ineligible')
    .map((item) => ({
      processorId: item.processorId,
      reasons: [...item.reasonCodes].sort(),
    }))

  return {
    headline: chosen
      ? `Processed with ${chosen.processorId}`
      : `No eligible processor for ${receipt.capability}`,
    chosen: chosen?.processorId,
    cacheLabel: receipt.cacheHit ? 'Reused an existing result' : undefined,
    reasons,
  }
}
