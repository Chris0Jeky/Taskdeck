import http from './http'

export interface ProposalRevision {
  id: string
  proposalId: string
  revisionNumber: number
  editorUserId: string
  revisedPayload: string
  revisedAt: string
  reason: string
  createdAt: string
}

export interface CreateRevisionPayload {
  revisedPayload: string
  reason: string
}

export const proposalRevisionsApi = {
  async createRevision(
    proposalId: string,
    payload: CreateRevisionPayload,
  ): Promise<ProposalRevision> {
    const { data } = await http.post<ProposalRevision>(
      `/automation/proposals/${encodeURIComponent(proposalId)}/revisions`,
      payload,
    )
    return data
  },

  async getRevisions(proposalId: string): Promise<ProposalRevision[]> {
    const { data } = await http.get<ProposalRevision[]>(
      `/automation/proposals/${encodeURIComponent(proposalId)}/revisions`,
    )
    return data
  },

  async getLatestRevision(proposalId: string): Promise<ProposalRevision | null> {
    try {
      const { data } = await http.get<ProposalRevision>(
        `/automation/proposals/${encodeURIComponent(proposalId)}/revisions/latest`,
      )
      return data
    } catch (e: unknown) {
      const err = e as { response?: { status?: number } }
      if (err?.response?.status === 404) return null
      throw e
    }
  },
}
