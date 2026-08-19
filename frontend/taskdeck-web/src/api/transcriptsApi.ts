import http from './http'

export interface TranscriptSegmentDto {
  startLine: number
  endLine: number
  speaker: string | null
  timestampMilliseconds: number | null
}

export interface TranscriptDto {
  id: string
  boardId: string | null
  /** Numeric System.Text.Json value of the backend CaptureSource enum. */
  captureSource: number
  /**
   * The canonical LF-normalized transcript text, returned whole (the backend caps a
   * transcript at 200,000 characters). Evidence spans are character offsets into
   * exactly this string, so it must never be re-normalized before slicing.
   */
  text: string
  segments: TranscriptSegmentDto[]
  createdFromCaptureId: string | null
  createdAt: string
}

export interface RequestOptions {
  signal?: AbortSignal
}

export const transcriptsApi = {
  /** Fetches one transcript owned by the authenticated user. Foreign ids return 404. */
  async getById(transcriptId: string, options?: RequestOptions): Promise<TranscriptDto> {
    const { data } = await http.get<TranscriptDto>(
      `/transcripts/${encodeURIComponent(transcriptId)}`,
      { signal: options?.signal },
    )
    return data
  },
}
