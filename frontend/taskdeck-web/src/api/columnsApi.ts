import http from './http'
import type { Column, CreateColumnDto, UpdateColumnDto } from '../types/board'

export const columnsApi = {
  async getColumns(boardId: string): Promise<Column[]> {
    const { data } = await http.get<Column[]>(`/boards/${boardId}/columns`)
    return data
  },

  async createColumn(boardId: string, column: CreateColumnDto): Promise<Column> {
    const { data } = await http.post<Column>(`/boards/${boardId}/columns`, column)
    return data
  },

  async updateColumn(boardId: string, columnId: string, column: UpdateColumnDto): Promise<Column> {
    const { data } = await http.patch<Column>(`/boards/${boardId}/columns/${columnId}`, column)
    return data
  },

  async deleteColumn(boardId: string, columnId: string): Promise<void> {
    await http.delete(`/boards/${boardId}/columns/${columnId}`)
  },

  async reorderColumns(boardId: string, columnIds: string[]): Promise<Column[]> {
    const { data } = await http.post<Column[]>(`/boards/${boardId}/columns/reorder`, {
      columnIds,
    })
    return data
  },
}
