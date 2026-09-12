import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { cardRelationsApi } from '../../api/cardRelationsApi'

vi.mock('../../api/http', () => ({ default: { get: vi.fn(), post: vi.fn() } }))

describe('cardRelationsApi', () => {
  beforeEach(() => vi.clearAllMocks())

  it('reads the board graph without preloading card details', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: { boardId: 'board', revision: 4, relations: [], canWrite: true } })

    await cardRelationsApi.get('board/a')

    expect(http.get).toHaveBeenCalledWith('/boards/board%2Fa/relations')
  })

  it('serializes one add proposal with the observed graph revision', async () => {
    vi.mocked(http.post).mockResolvedValue({ data: { id: 'proposal-1' } })

    await cardRelationsApi.addProposal({
      boardId: 'board', cardId: 'a', relatedCardId: 'b', relationType: 'depends-on', expectedRevision: 7,
    })

    const [url, request] = vi.mocked(http.post).mock.calls[0] as [string, Record<string, unknown>]
    expect(url).toBe('/automation/proposals')
    expect(request).toMatchObject({ sourceType: 2, riskLevel: 0, boardId: 'board' })
    const operations = request.operations as Array<Record<string, unknown>>
    expect(operations).toHaveLength(1)
    const operation = operations[0]
    expect(operation).toMatchObject({ sequence: 0, actionType: 'add-relation', targetType: 'card', targetId: 'a' })
    expect(JSON.parse(operation.parameters as string)).toEqual({
      boardId: 'board', cardId: 'a', relatedCardId: 'b', relationType: 'depends-on', expectedRevision: 7,
    })
  })

  it('serializes removal as the same one-operation proposal envelope', async () => {
    vi.mocked(http.post).mockResolvedValue({ data: { id: 'proposal-2' } })

    await cardRelationsApi.removeProposal({
      boardId: 'board', cardId: 'source', relatedCardId: 'target', relationType: 'blocks', expectedRevision: 9,
    })

    const request = vi.mocked(http.post).mock.calls[0][1] as { operations: Array<Record<string, unknown>> }
    expect(request.operations).toHaveLength(1)
    expect(request.operations[0].actionType).toBe('remove-relation')
    expect(JSON.parse(request.operations[0].parameters as string).expectedRevision).toBe(9)
  })

  it('uses UUID-compatible IDs when randomUUID is unavailable on a LAN origin', async () => {
    const originalCrypto = globalThis.crypto
    vi.stubGlobal('crypto', {
      getRandomValues: originalCrypto.getRandomValues.bind(originalCrypto),
    })
    vi.mocked(http.post)
      .mockResolvedValueOnce({ data: { id: 'proposal-add' } })
      .mockResolvedValueOnce({ data: { id: 'proposal-remove' } })

    try {
      const input = {
        boardId: 'board', cardId: 'source', relatedCardId: 'target',
        relationType: 'depends-on' as const, expectedRevision: 12,
      }

      await cardRelationsApi.addProposal(input)
      await cardRelationsApi.removeProposal(input)

      const requests = vi.mocked(http.post).mock.calls.map(([, request]) => request as {
        sourceType: number
        riskLevel: number
        operations: Array<Record<string, unknown>>
      })
      expect(requests).toHaveLength(2)
      expect(requests.map(request => request.sourceType)).toEqual([2, 2])
      expect(requests.map(request => request.riskLevel)).toEqual([0, 0])
      for (const request of requests) expect(request.operations).toHaveLength(1)

      const correlations = vi.mocked(http.post).mock.calls.map(([, request]) =>
        (request as { correlationId: string }).correlationId)
      const allIds = [...correlations, ...requests.map(request => request.operations[0]!.idempotencyKey as string)]
      expect(allIds).toHaveLength(4)
      expect(new Set(allIds).size).toBe(4)
      for (const id of allIds) expect(id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i)

      expect(requests.map(request => request.operations[0]!.actionType)).toEqual([
        'add-relation', 'remove-relation',
      ])
      for (const request of requests) {
        expect(JSON.parse(request.operations[0]!.parameters as string)).toMatchObject({
          boardId: 'board', cardId: 'source', relatedCardId: 'target',
          relationType: 'depends-on', expectedRevision: 12,
        })
      }
    } finally {
      vi.stubGlobal('crypto', originalCrypto)
    }
  })
})
