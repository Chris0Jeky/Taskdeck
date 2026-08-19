import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { transcriptsApi } from '../../api/transcriptsApi'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
  },
}))

describe('transcriptsApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('fetches a transcript through the shared http client', async () => {
    const transcript = {
      id: 't-1',
      boardId: null,
      captureSource: 2,
      text: 'Ada: ship the export fix',
      segments: [{ startLine: 0, endLine: 0, speaker: 'Ada', timestampMilliseconds: 0 }],
      createdFromCaptureId: null,
      createdAt: '2026-08-19T10:00:00Z',
    }
    vi.mocked(http.get).mockResolvedValue({ data: transcript })

    const result = await transcriptsApi.getById('t-1')

    expect(http.get).toHaveBeenCalledWith('/transcripts/t-1', { signal: undefined })
    expect(result).toEqual(transcript)
  })

  it('encodes the transcript id and forwards the abort signal', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: null })
    const controller = new AbortController()

    await transcriptsApi.getById('a/b?c', { signal: controller.signal })

    expect(http.get).toHaveBeenCalledWith('/transcripts/a%2Fb%3Fc', { signal: controller.signal })
  })
})
