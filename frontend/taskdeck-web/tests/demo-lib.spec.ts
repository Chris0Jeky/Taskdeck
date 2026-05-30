import { describe, expect, it } from 'vitest'

import { cleanupDemoBoards } from '../scripts/demo-lib.mjs'

describe('demo harness board cleanup', () => {
  it('normalizes paginated board responses before filtering demo boards', async () => {
    const deletedPaths: string[] = []
    const requestedPaths: string[] = []

    const api = {
      async get(path: string) {
        requestedPaths.push(path)
        return {
          items: [
            { id: 'scratch-board', name: 'DEMO: Scratch Board' },
            { id: 'canonical-board', name: 'DEMO: Capture Loop' },
            { id: 'personal-board', name: 'Personal Board' },
          ],
          hasMore: false,
        }
      },
      async del(path: string) {
        deletedPaths.push(path)
      },
    }

    const result = await cleanupDemoBoards(api, { includeArchived: true })

    expect(requestedPaths).toEqual(['/boards?includeArchived=true&offset=0&limit=200'])
    expect(deletedPaths).toEqual(['/boards/scratch-board'])
    expect(result).toMatchObject({
      archived: 1,
      skipped: [],
      candidates: [{ id: 'scratch-board', name: 'DEMO: Scratch Board' }],
    })
  })
})
