import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { starterPacksApi } from '../../api/starterPacksApi'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}))

describe('starterPacksApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('fetches starter-pack catalog for a board', async () => {
    const catalogPayload = [
      {
        id: 'common-labels-core',
        category: 'label-pack',
        title: 'Common Labels Core',
        summary: 'Reusable labels',
        highlights: ['Priority and blocked labels'],
        manifest: {
          schemaVersion: '1.0',
          packId: 'common-labels-core',
          displayName: 'Common Labels Core',
          compatibility: {
            minTaskdeckVersion: '1.0.0',
            requiredFeatures: ['boards', 'labels'],
          },
          tags: ['starter'],
          labels: [],
          columns: [{ name: 'Backlog', position: 0 }],
          templates: [],
          seedCards: [],
        },
      },
    ]

    vi.mocked(http.get).mockResolvedValue({ data: catalogPayload })

    const result = await starterPacksApi.getCatalog('board-1')

    expect(http.get).toHaveBeenCalledWith('/boards/board-1/starter-packs/catalog')
    expect(result).toEqual(catalogPayload)
  })

  it('posts starter-pack apply payload to the board endpoint', async () => {
    const payload = {
      boardId: 'board-1',
      packId: 'engineering-onboarding',
      dryRun: false,
      applied: true,
      actions: [],
      conflicts: [],
    }

    vi.mocked(http.post).mockResolvedValue({ data: payload })

    const result = await starterPacksApi.applyStarterPack('board-1', {
      manifest: {
        schemaVersion: '1.0',
        packId: 'engineering-onboarding',
        displayName: 'Engineering Onboarding',
        compatibility: {
          minTaskdeckVersion: '1.0.0',
          requiredFeatures: ['boards'],
        },
        tags: ['starter'],
        labels: [],
        columns: [{ name: 'Backlog', position: 0 }],
        templates: [],
        seedCards: [],
      },
      dryRun: false,
    })

    expect(http.post).toHaveBeenCalledWith('/boards/board-1/starter-packs/apply', expect.any(Object))
    expect(result).toEqual(payload)
  })
})
