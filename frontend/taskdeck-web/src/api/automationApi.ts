import http from './http'
import type {
  BatchApproveProposalSelection,
  BatchApproveProposalsResult,
  BatchExecuteProposalSelection,
  BatchExecuteProposalsResult,
  Proposal,
  ProposalFilters,
} from '../types/automation'
import { buildQueryString } from '../utils/queryBuilder'

interface AutomationReadOptions {
  signal?: AbortSignal
  skipRetry?: boolean
  /**
   * Statuses that are an expected part of THIS call's contract, so the shared
   * response interceptor stops logging them as 'API Error:' (#2214 item 7).
   * The background review poll re-reads a hash-pinned proposal by id and turns
   * 400/403/404 into an explicit "pin unavailable" outcome; logging those as
   * failures reports a handled result as a defect on every tick. Opt-in per
   * call site, exactly like `proposalDeepReviewApi.getProvenanceMetadata` --
   * the explicit deep-link read still logs and surfaces its failures.
   */
  expectedStatuses?: number[]
}

export const automationApi = {
  /**
   * `options` exists for the background review-queue poll (#2194): it opts out
   * of the shared retry interceptor and carries an abort signal so a poll can be
   * cancelled when the surface is left. Ordinary callers pass nothing and keep
   * the retrying, uncancellable behaviour.
   *
   * The config is forwarded ONLY when one was supplied. Passing `undefined`
   * unconditionally is behaviourally identical to axios but NOT identical to
   * observe: it changes the arity of every existing caller's `http.get` call,
   * which is what the exact-args assertion below in `automationApi.spec.ts`
   * pins -- and it went red in CI. Keeping the one-argument shape leaves every
   * other caller byte-identical, which is the whole promise of an optional arg.
   */
  async getProposals(
    filters?: ProposalFilters,
    options?: AutomationReadOptions,
  ): Promise<Proposal[]> {
    const url = `/automation/proposals${buildQueryString(filters)}`
    const { data } = options
      ? await http.get<Proposal[]>(url, options)
      : await http.get<Proposal[]>(url)
    return data
  },

  async getProposal(id: string, options?: AutomationReadOptions): Promise<Proposal> {
    const url = `/automation/proposals/${encodeURIComponent(id)}`
    const { data } = options
      ? await http.get<Proposal>(url, options)
      : await http.get<Proposal>(url)
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

  /**
   * Per-proposal batch execute (`#1307`, q-14 C). Not all-or-none: the 200 body carries one outcome
   * row per requested proposal, in request order. Each item's idempotency key travels in the body
   * rather than a header because there is one key per proposal, not one per request.
   */
  async executeProposals(
    proposals: BatchExecuteProposalSelection[],
  ): Promise<BatchExecuteProposalsResult> {
    const { data } = await http.post<BatchExecuteProposalsResult>(
      '/automation/proposals/execute',
      { proposals },
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
