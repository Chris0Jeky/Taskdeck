import http from './http'
import type { AgentProfile, AgentRun, AgentRunDetail } from '../types/agent'

export const agentApi = {
  async listProfiles(): Promise<AgentProfile[]> {
    const { data } = await http.get<AgentProfile[]>('/agents')
    return data
  },

  async getProfile(id: string): Promise<AgentProfile> {
    const { data } = await http.get<AgentProfile>(`/agents/${encodeURIComponent(id)}`)
    return data
  },

  async listRuns(agentId: string, limit = 100): Promise<AgentRun[]> {
    const { data } = await http.get<AgentRun[]>(
      `/agents/${encodeURIComponent(agentId)}/runs?limit=${limit}`,
    )
    return data
  },

  async getRunDetail(agentId: string, runId: string): Promise<AgentRunDetail> {
    const { data } = await http.get<AgentRunDetail>(
      `/agents/${encodeURIComponent(agentId)}/runs/${encodeURIComponent(runId)}`,
    )
    return data
  },
}
