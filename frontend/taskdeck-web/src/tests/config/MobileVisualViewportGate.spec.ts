import { describe, expect, it } from 'vitest'
import requiredE2eWorkflow from '../../../../../.github/workflows/reusable-e2e-smoke.yml?raw'
import requiredPlaywrightConfig from '../../../playwright.required.config.ts?raw'
import mobileResponsiveSpec from '../../../tests/e2e/mobile-responsive.spec.ts?raw'

/**
 * The contracted VisualViewport journey is the browser-level proof that
 * CardModal and the shared TdDialog keep their actions above a software
 * keyboard. It used to live only in the mobile projects, while the required PR
 * gate selected desktop Chromium and therefore skipped every `@mobile` test.
 *
 * Keep the three pieces of the bounded gate together:
 *
 * 1. a required-CI config reuses the existing desktop Chromium project and adds
 *    one Pixel 7 project selected by the exact geometry-journey title;
 * 2. required E2E invokes that config once, so the server stack is not launched
 *    a second time;
 * 3. the selected title still identifies exactly one browser journey.
 *
 * This is intentionally a source contract. Whether a workflow selects a
 * Playwright project is configuration topology, not DOM behaviour, and a
 * mounted component cannot prove it.
 */
describe('required mobile visual-viewport geometry gate (GH-1867)', () => {
  it('defines a bounded Pixel 7 project beside the existing Chromium gate', () => {
    expect(requiredPlaywrightConfig).toMatch(
      /baseConfig\.projects\?\.find\(\(project\) => project\.name === ['"]chromium['"]\)/,
    )
    expect(requiredPlaywrightConfig).toMatch(
      /name:\s*['"]mobile-required['"][\s\S]*?devices\[['"]Pixel 7['"]\][\s\S]*?grep:\s*\/@mobile card editing modal follows a contracted visual viewport\//,
    )
    expect(requiredPlaywrightConfig).toMatch(
      /projects:\s*\[desktopChromium,\s*requiredMobileGeometry\]/,
    )
  })

  it('uses the bounded config in the existing required Playwright invocation', () => {
    expect(requiredE2eWorkflow).toMatch(
      /npx playwright test\s+--config=playwright\.required\.config\.ts\s+--reporter=line/,
    )
  })

  it('selects one existing contracted visual-viewport journey', () => {
    const title = '@mobile card editing modal follows a contracted visual viewport'
    expect(mobileResponsiveSpec.split(title)).toHaveLength(2)
  })
})
