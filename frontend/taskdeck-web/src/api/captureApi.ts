import http from './http'
import { buildQueryString } from '../utils/queryBuilder'
import type {
  CaptureItem,
  CaptureItemSummary,
  CaptureListQuery,
  CreateCaptureItemDto,
} from '../types/capture'

function encodePathSegment(value: string): string {
  return encodeURIComponent(value)
}

export const captureApi = {
  async createItem(dto: CreateCaptureItemDto): Promise<CaptureItem> {
    const { data } = await http.post<CaptureItem>('/capture/items', dto)
    return data
  },

  async listItems(query?: CaptureListQuery): Promise<CaptureItemSummary[]> {
    const { data } = await http.get<CaptureItemSummary[]>(`/capture/items${buildQueryString(query)}`)
    return data
  },

  async getItem(itemId: string): Promise<CaptureItem> {
    const pathItemId = encodePathSegment(itemId)
    const { data } = await http.get<CaptureItem>(`/capture/items/${pathItemId}`)
    return data
  },

  async ignoreItem(itemId: string): Promise<void> {
    const pathItemId = encodePathSegment(itemId)
    await http.post(`/capture/items/${pathItemId}/ignore`)
  },

  async cancelItem(itemId: string): Promise<void> {
    const pathItemId = encodePathSegment(itemId)
    await http.post(`/capture/items/${pathItemId}/cancel`)
  },
}
