import { beforeEach, describe, expect, it, vi } from 'vitest'
import { hideDynamicContent } from '../../../tests/visual/visual-test-helpers'
// Read through Vite's `?raw` loader rather than node:fs: this project excludes node types and
// `?raw` resolves relative to this file rather than the vitest working directory.
import visualConfigSource from '../../../playwright.visual.config.ts?raw'

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

describe('visual regression browser environment', () => {
  it('pins the browser locale and timezone in the shared use block', () => {
    const useBlock = visualConfigSource.match(/\r?\n {2}use: \{[\s\S]*?\r?\n {2}\},/)?.[0]

    expect(useBlock).toBeDefined()
    expect(useBlock).toMatch(/locale: 'en-US',/)
    expect(useBlock).toMatch(/timezoneId: 'UTC',/)
  })
})
