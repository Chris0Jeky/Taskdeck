import { beforeEach, describe, expect, it, vi } from 'vitest'
import { hideDynamicContent } from '../../../tests/visual/visual-test-helpers'

describe('visual regression contracts', () => {
  beforeEach(() => {
    document.head.querySelectorAll('style[data-visual-test]').forEach((style) => style.remove())
  })

  it('hides dynamic content without shifting the page', async () => {
    const page = {
      evaluate: vi.fn(async (callback: () => void) => callback()),
    } as unknown as Parameters<typeof hideDynamicContent>[0]

    await hideDynamicContent(page)

    const style = document.head.querySelector('style[data-visual-test]')
    expect(style).not.toBeNull()
    expect(style?.textContent).toContain('[data-toast-id]')
    expect(style?.textContent).not.toContain('translateX')
    expect(style?.textContent).not.toMatch(/body\s*\{\s*transform\s*:/)
  })
})
