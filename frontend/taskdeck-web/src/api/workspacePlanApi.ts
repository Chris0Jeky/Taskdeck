import http from './http'

export interface PlanReference { boardId: string; cardId: string; plannedDate: string }
export interface PlanCard {
  boardId: string
  cardId: string
  available: boolean
  title: string | null
  boardName: string | null
  columnName: string | null
  dueDate: string | null
  isBlocked: boolean
  blockReason: string | null
}
export interface PlanEntry extends PlanReference, PlanCard {}
export interface WorkspacePlan {
  revision: number
  entries: PlanEntry[]
  lastWorked: (PlanCard & { workedAt: string }) | null
}

export const workspacePlanApi = {
  async get(): Promise<WorkspacePlan> { return (await http.get<WorkspacePlan>('/workspace/plan')).data },
  async save(expectedRevision: number, entries: PlanReference[]): Promise<WorkspacePlan> {
    return (await http.put<WorkspacePlan>('/workspace/plan', { expectedRevision, entries })).data
  },
  async focus(expectedRevision: number, boardId: string, cardId: string): Promise<WorkspacePlan> {
    return (await http.post<WorkspacePlan>('/workspace/plan/focus', { expectedRevision, boardId, cardId })).data
  },
}
