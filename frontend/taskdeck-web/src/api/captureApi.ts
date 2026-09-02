import http from './http'
import { buildQueryString } from '../utils/queryBuilder'
import type {
  BatchTriageItemAction,
  BatchTriageResult,
  CaptureItem,
  CaptureItemSummary,
  CaptureListQuery,
  CaptureTriageEnqueueResult,
  CreateCaptureItemDto,
  UpdateCaptureSuggestionDto,
} from '../types/capture'

function encodePathSegment(value: string): string {
  return encodeURIComponent(value)
}

export type CaptureReadOptions = {
  signal?: AbortSignal
  skipRetry?: boolean
}

export const captureApi = {
  async createItem(dto: CreateCaptureItemDto): Promise<CaptureItem> {
    const { data } = await http.post<CaptureItem>('/capture/items', dto)
    return data
  },

  async listItems(
    query?: CaptureListQuery,
    options?: CaptureReadOptions,
  ): Promise<CaptureItemSummary[]> {
    const url = `/capture/items${buildQueryString(query)}`
    const { data } = options
      ? await http.get<CaptureItemSummary[]>(url, options)
      : await http.get<CaptureItemSummary[]>(url)
    return data
  },

  async getItem(itemId: string, options?: CaptureReadOptions): Promise<CaptureItem> {
    const pathItemId = encodePathSegment(itemId)
    const url = `/capture/items/${pathItemId}`
    const { data } = options
      ? await http.get<CaptureItem>(url, options)
      : await http.get<CaptureItem>(url)
    return data
  },

  async keepItem(itemId: string): Promise<CaptureItem> {
    const pathItemId = encodePathSegment(itemId)
    const { data } = await http.post<CaptureItem>(`/capture/items/${pathItemId}/keep`)
    return data
  },

  async archiveItem(itemId: string): Promise<CaptureItem> {
    const pathItemId = encodePathSegment(itemId)
    const { data } = await http.post<CaptureItem>(`/capture/items/${pathItemId}/archive`)
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

  async enqueueTriage(itemId: string, boardId?: string | null): Promise<CaptureTriageEnqueueResult> {
    const pathItemId = encodePathSegment(itemId)
    // Board-less captures (Home quick-capture) must supply a target board so the server can link it
    // and triage in one step instead of rejecting with a 400 (#1764).
    const body = boardId ? { boardId } : undefined
    const { data } = await http.post<CaptureTriageEnqueueResult>(`/capture/items/${pathItemId}/triage`, body)
    return data
  },

  async batchTriage(items: BatchTriageItemAction[]): Promise<BatchTriageResult> {
    const { data } = await http.post<BatchTriageResult>('/capture/items/batch-triage', { items })
    return data
  },

  async updateSuggestion(itemId: string, dto: UpdateCaptureSuggestionDto): Promise<CaptureItem> {
    const pathItemId = encodePathSegment(itemId)
    const { data } = await http.put<CaptureItem>(`/capture/items/${pathItemId}/suggestion`, dto)
    return data
  },
}
