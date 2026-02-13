import http from './http'
import type { Proposal, ProposalFilters } from '../types/automation'
import { buildQueryString } from '../utils/queryBuilder'

export const automationApi = {
  async getProposals(filters?: ProposalFilters): Promise<Proposal[]> {
    const { data } = await http.get<Proposal[]>(`/automation/proposals${buildQueryString(filters)}`)
    return data
  },

  async getProposal(id: string): Promise<Proposal> {
    const { data } = await http.get<Proposal>(`/automation/proposals/${encodeURIComponent(id)}`)
    return data
  },

  async approveProposal(id: string): Promise<Proposal> {
    const { data } = await http.post<Proposal>(`/automation/proposals/${encodeURIComponent(id)}/approve`)
    return data
  },

  async rejectProposal(id: string, reason: string): Promise<Proposal> {
    const { data } = await http.post<Proposal>(`/automation/proposals/${encodeURIComponent(id)}/reject`, { reason })
    return data
  },

  async executeProposal(id: string, idempotencyKey: string): Promise<Proposal> {
    const { data } = await http.post<Proposal>(
      `/automation/proposals/${encodeURIComponent(id)}/execute`,
      null,
      {
        headers: {
          'Idempotency-Key': idempotencyKey,
        },
      }
    )
    return data
  },

  async getProposalDiff(id: string): Promise<string> {
    const { data } = await http.get<{ diff: string }>(`/automation/proposals/${encodeURIComponent(id)}/diff`)
    return data.diff
  },
}
