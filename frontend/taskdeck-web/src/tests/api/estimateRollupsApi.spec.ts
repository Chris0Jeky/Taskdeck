import { beforeEach, describe, expect, it, vi } from 'vitest'
import { estimateRollupsApi } from '../../api/estimateRollupsApi'
import http, { BOARD_REQUEST_TIMEOUT_MS } from '../../api/http'

vi.mock('../../api/http', () => ({
  default: { get: vi.fn() },
  BOARD_REQUEST_TIMEOUT_MS: 10_000,
}))

describe('estimateRollupsApi bounded reads', () => {
  beforeEach(() => vi.resetAllMocks())

  it('uses the shared client with a finite timeout and no automatic retry', async () => {
    const data = { boardId: 'board' }
    vi.mocked(http.get).mockResolvedValue({ data })
    expect(await estimateRollupsApi.get('board')).toBe(data)
    expect(http.get).toHaveBeenCalledWith('/boards/board/estimate-rollups', {
      signal: undefined, timeout: BOARD_REQUEST_TIMEOUT_MS, skipRetry: true,
    })
  })

  it('passes through the exact caller-owned signal', async () => {
    const controller = new AbortController()
    vi.mocked(http.get).mockResolvedValue({ data: {} })
    await estimateRollupsApi.get('board', { signal: controller.signal })
    expect(http.get).toHaveBeenCalledWith('/boards/board/estimate-rollups', {
      signal: controller.signal, timeout: BOARD_REQUEST_TIMEOUT_MS, skipRetry: true,
    })
  })

  it.each(['ECONNABORTED', 'ERR_CANCELED'])('preserves %s failures for the request owner', async (code) => {
    const failure = { code }
    vi.mocked(http.get).mockRejectedValue(failure)
    let rejected = false
    let received: unknown
    try {
      await estimateRollupsApi.get('board')
    } catch (error) {
      rejected = true
      received = error
    }
    expect(rejected).toBe(true)
    expect(received).toBe(failure)
    expect(http.get).toHaveBeenCalledTimes(1)
  })
})
