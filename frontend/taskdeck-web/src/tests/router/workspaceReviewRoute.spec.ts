import { describe, expect, it } from 'vitest'
import router from '../../router'

describe('workspace review route', () => {
  it('loads the Paper deep-review surface', async () => {
    const route = router.getRoutes().find((item) => item.name === 'workspace-review')
    const component = route?.components?.default

    expect(typeof component).toBe('function')

    const module = await (component as () => Promise<{ default: { __name?: string } }>)()
    expect(module.default.__name).toBe('PaperReviewView')
  })
})
