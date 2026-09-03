import http, { type BoardReadOptions } from './http'
import type { Label, CreateLabelDto, UpdateLabelDto } from '../types/board'

export const labelsApi = {
  async getLabels(boardId: string, options?: BoardReadOptions): Promise<Label[]> {
    const url = `/boards/${boardId}/labels`
    const { data } = options ? await http.get<Label[]>(url, options) : await http.get<Label[]>(url)
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
