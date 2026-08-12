import type {
  Proposal,
  ProposalRiskLevelValue,
  ProposalSourceTypeValue,
  ProposalStatusValue,
} from '../types/automation'

const proposalStatusByIndex = ['PendingReview', 'Approved', 'Rejected', 'Applied', 'Failed', 'Expired', 'Dismissed'] as const
const proposalSourceByIndex = ['Queue', 'Chat', 'Manual'] as const
const proposalRiskByIndex = ['Low', 'Medium', 'High', 'Critical'] as const

export function normalizeProposalStatus(value: ProposalStatusValue): typeof proposalStatusByIndex[number] {
  if (typeof value === 'number') {
    return proposalStatusByIndex[value] ?? 'PendingReview'
  }

  const found = proposalStatusByIndex.find(v => v.toLowerCase() === value.toLowerCase())
  return found ?? 'PendingReview'
}

export function normalizeProposalSourceType(value: ProposalSourceTypeValue): typeof proposalSourceByIndex[number] {
  if (typeof value === 'number') {
    return proposalSourceByIndex[value] ?? 'Manual'
  }

  const found = proposalSourceByIndex.find(v => v.toLowerCase() === value.toLowerCase())
  return found ?? 'Manual'
}

export function normalizeProposalRiskLevel(value: ProposalRiskLevelValue): typeof proposalRiskByIndex[number] {
  if (typeof value === 'number') {
    return proposalRiskByIndex[value] ?? 'Low'
  }

  const found = proposalRiskByIndex.find(v => v.toLowerCase() === value.toLowerCase())
  return found ?? 'Low'
}

const proposalRiskRank: Record<typeof proposalRiskByIndex[number], number> = {
  Low: 0,
  Medium: 1,
  High: 2,
  Critical: 3,
}

/**
 * Return a stable risk-ordered copy for Paper's review queue.
 *
 * The source index keeps the existing queue order within a risk tier, so an
 * unchanged refresh cannot reshuffle equal-risk items. The input array is
 * never mutated.
 */
export function sortProposalsByRisk(proposals: readonly Proposal[]): Proposal[] {
  return proposals
    .map((proposal, index) => ({ proposal, index }))
    .sort((a, b) => {
      const riskDifference =
        proposalRiskRank[normalizeProposalRiskLevel(a.proposal.riskLevel)] -
        proposalRiskRank[normalizeProposalRiskLevel(b.proposal.riskLevel)]
      if (riskDifference !== 0) return riskDifference

      return a.index - b.index
    })
    .map(({ proposal }) => proposal)
}
