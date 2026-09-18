import { defineConfig, devices } from '@playwright/test'
import baseConfig from './playwright.config'

/**
 * Required-CI Playwright topology.
 *
 * Reuse the complete desktop Chromium project from the canonical configuration,
 * then add one Chromium device-emulation project for the browser-level
 * VisualViewport geometry journey. Keeping both projects in one Playwright
 * invocation means its backend/frontend webServer stack is started only once.
 *
 * The full `mobile-chrome` and `mobile-safari` projects remain in
 * `playwright.config.ts` for nightly/manual coverage; this file is intentionally
 * not another mobile matrix.
 */
const desktopChromium = baseConfig.projects?.find((project) => project.name === 'chromium')
if (!desktopChromium) {
  throw new Error('[required e2e config] canonical Chromium project is missing')
}

const requiredMobileGeometry = {
  name: 'mobile-required',
  use: { ...devices['Pixel 7'] },
  grep: /@mobile card editing modal follows a contracted visual viewport/,
  grepInvert: /@quarantine/,
}

export default defineConfig({
  ...baseConfig,
  projects: [desktopChromium, requiredMobileGeometry],
})
