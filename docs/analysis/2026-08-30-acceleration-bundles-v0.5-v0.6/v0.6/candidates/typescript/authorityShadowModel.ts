
export type AuthorityShadowDecision = {
  result: 'ineligible' | 'would-allow' | 'would-deny'
  reasonCodes: string[]
  policyVersion: string
  evaluatedAt: string
}

export type AuthorityShadowView = {
  title: string
  explanation: string
  executionEnabled: false
}

export function presentAuthorityShadow(
  decision: AuthorityShadowDecision,
): AuthorityShadowView {
  const explanation =
    decision.result === 'would-allow'
      ? 'This policy would have allowed the operation. It was not executed.'
      : decision.reasonCodes.length > 0
        ? `Shadow policy did not allow the operation: ${decision.reasonCodes.join(', ')}.`
        : 'Shadow policy did not allow the operation.'

  return {
    title: 'Authority shadow result',
    explanation,
    executionEnabled: false,
  }
}
