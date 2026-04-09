import http from './http'
import { normalizeRunStatus, normalizeScopeType } from '../types/agent'
import type {
  AgentProfile,
  AgentRun,
  AgentRunDetail,
  AgentRunStatusValue,
  AgentScopeTypeValue,
} from '../types/agent'

/** Raw profile shape from backend (enums may be numeric) */
interface RawAgentProfile extends Omit<AgentProfile, 'scopeType'> {
  scopeType: AgentScopeTypeValue
}

/** Raw run shape from backend (enums may be numeric) */
interface RawAgentRun extends Omit<AgentRun, 'status'> {
  status: AgentRunStatusValue
}

interface RawAgentRunDetail extends Omit<AgentRunDetail, 'status'> {
  status: AgentRunStatusValue
}

function normalizeProfile(raw: RawAgentProfile): AgentProfile {
  return {
    ...raw,
    scopeType: normalizeScopeType(raw.scopeType),
  }
}

function normalizeRun(raw: RawAgentRun): AgentRun {
  return {
    ...raw,
    status: normalizeRunStatus(raw.status),
  }
}

function normalizeRunDetail(raw: RawAgentRunDetail): AgentRunDetail {
  return {
    ...raw,
    status: normalizeRunStatus(raw.status),
  }
}

export const agentApi = {
  async listProfiles(): Promise<AgentProfile[]> {
    const { data } = await http.get<RawAgentProfile[]>('/agents')
    return data.map(normalizeProfile)
  },

  async getProfile(id: string): Promise<AgentProfile> {
    const { data } = await http.get<RawAgentProfile>(`/agents/${encodeURIComponent(id)}`)
    return normalizeProfile(data)
  },

  async listRuns(agentId: string, limit = 100): Promise<AgentRun[]> {
    const { data } = await http.get<RawAgentRun[]>(
      `/agents/${encodeURIComponent(agentId)}/runs?limit=${limit}`,
    )
    return data.map(normalizeRun)
  },

  async getRunDetail(agentId: string, runId: string): Promise<AgentRunDetail> {
    const { data } = await http.get<RawAgentRunDetail>(
      `/agents/${encodeURIComponent(agentId)}/runs/${encodeURIComponent(runId)}`,
    )
    return normalizeRunDetail(data)
  },
}
