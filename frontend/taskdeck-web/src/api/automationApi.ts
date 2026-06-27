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

  async rejectProposal(id: string, reason: string | null): Promise<Proposal> {
    const { data } = await http.post<Proposal>(`/automation/proposals/${encodeURIComponent(id)}/reject`, { reason })
    return data
  },

  async deferProposal(id: string, durationMinutes?: number): Promise<Proposal> {
    const body = durationMinutes != null ? { durationMinutes } : undefined
    const { data } = await http.post<Proposal>(
      `/automation/proposals/${encodeURIComponent(id)}/defer`,
      body,
    )
    return data
  },

  async reportBadSuggestion(proposalId: string, reason?: string): Promise<void> {
    // Content-free negative feedback. Returns 204; nothing to map back into board state.
    await http.post(`/automation/proposals/${encodeURIComponent(proposalId)}/feedback`, {
      reason: reason ?? null,
    })
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

  async dismissProposals(ids: string[]): Promise<{ dismissed: number }> {
    const { data } = await http.post<{ dismissed: number }>('/automation/proposals/dismiss', { ids })
    return data
  },
}
