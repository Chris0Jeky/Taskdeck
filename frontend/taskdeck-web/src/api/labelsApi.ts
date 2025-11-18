import http from './http'
import type { Label, CreateLabelDto, UpdateLabelDto } from '../types/board'

export const labelsApi = {
  async getLabels(boardId: string): Promise<Label[]> {
    const { data } = await http.get<Label[]>(`/boards/${boardId}/labels`)
    return data
  },

  async createLabel(boardId: string, label: CreateLabelDto): Promise<Label> {
    const { data } = await http.post<Label>(`/boards/${boardId}/labels`, label)
    return data
  },

  async updateLabel(boardId: string, labelId: string, label: UpdateLabelDto): Promise<Label> {
    const { data } = await http.patch<Label>(`/boards/${boardId}/labels/${labelId}`, label)
    return data
  },

  async deleteLabel(boardId: string, labelId: string): Promise<void> {
    await http.delete(`/boards/${boardId}/labels/${labelId}`)
  },
}
