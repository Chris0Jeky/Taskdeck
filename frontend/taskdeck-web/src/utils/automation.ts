import type { ProposalRiskLevelValue, ProposalSourceTypeValue, ProposalStatusValue } from '../types/automation'

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
