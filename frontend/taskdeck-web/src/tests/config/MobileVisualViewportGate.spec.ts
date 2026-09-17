import { describe, expect, it } from 'vitest'
import requiredE2eWorkflow from '../../../../../.github/workflows/reusable-e2e-smoke.yml?raw'
import playwrightConfig from '../../../playwright.config.ts?raw'
import mobileResponsiveSpec from '../../../tests/e2e/mobile-responsive.spec.ts?raw'

/**
 * The contracted VisualViewport journey is the browser-level proof that
 * CardModal and the shared TdDialog keep their actions above a software
 * keyboard. It used to live only in the mobile projects, while the required PR
 * gate selected desktop Chromium and therefore skipped every `@mobile` test.
 *
 * Keep the three pieces of the bounded gate together:
 *
 * 1. one Pixel 7 Chromium project selects only `@mobile-required` journeys;
 * 2. required E2E invokes that project in the same Playwright process as the
 *    existing desktop smoke project, so the server stack is not launched twice;
 * 3. exactly the contracted visual-viewport journey carries the required tag.
 *
 * This is intentionally a source contract. Whether a workflow selects a
 * Playwright project is configuration topology, not DOM behaviour, and a
 * mounted component cannot prove it.
 */
describe('required mobile visual-viewport geometry gate (GH-1867)', () => {
  it('defines a bounded Pixel 7 project for required mobile geometry', () => {
    expect(playwrightConfig).toMatch(
      /name:\s*['"]mobile-required['"][\s\S]*?devices\[['"]Pixel 7['"]\][\s\S]*?grep:\s*\/@mobile-required\//,
    )
  })

  it('runs the bounded mobile project in the existing required Playwright invocation', () => {
    expect(requiredE2eWorkflow).toMatch(
      /npx playwright test\s+--project=chromium\s+--project=mobile-required\s+--reporter=line/,
    )
  })

  it('gates only the contracted visual-viewport journey', () => {
    expect(mobileResponsiveSpec.match(/@mobile-required/g) ?? []).toHaveLength(1)
    expect(mobileResponsiveSpec).toMatch(
      /test\(['"]@mobile @mobile-required card editing modal follows a contracted visual viewport['"]/,
    )
  })
})
