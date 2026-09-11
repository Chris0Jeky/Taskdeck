import http from './http'

export interface CardDependency { cardId: string; dependsOnCardId: string }
export interface BoardDependencies { boardId: string; revision: number; edges: CardDependency[]; canWrite: boolean }
export const boardDependenciesApi = {
  async get(boardId: string): Promise<BoardDependencies> {
    return (await http.get<BoardDependencies>(`/boards/${boardId}/dependencies`)).data
  },
  async save(boardId: string, expectedRevision: number, edges: CardDependency[]): Promise<BoardDependencies> {
    return (await http.put<BoardDependencies>(`/boards/${boardId}/dependencies`, { expectedRevision, edges })).data
  },
}
