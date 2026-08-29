import http from './http'
import type {
  BatchApproveProposalSelection,
  BatchApproveProposalsResult,
  Proposal,
  ProposalFilters,
} from '../types/automation'
import { buildQueryString } from '../utils/queryBuilder'

export const automationApi = {
  /**
   * `options` exists for the background review-queue poll (#2194): it opts out
   * of the shared retry interceptor and carries an abort signal so a poll can be
   * cancelled when the surface is left. Ordinary callers pass nothing and keep
   * the retrying, uncancellable behaviour.
   */
  async getProposals(
    filters?: ProposalFilters,
    options?: { signal?: AbortSignal; skipRetry?: boolean },
  ): Promise<Proposal[]> {
    const { data } = await http.get<Proposal[]>(
      `/automation/proposals${buildQueryString(filters)}`,
      options,
    )
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

  async approveProposals(
    proposals: BatchApproveProposalSelection[],
  ): Promise<BatchApproveProposalsResult> {
    const { data } = await http.post<BatchApproveProposalsResult>(
      '/automation/proposals/approve',
      { proposals },
    )
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
