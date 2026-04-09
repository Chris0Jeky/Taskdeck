/**
 * Shared helpers for visual regression tests.
 *
 * These utilities standardize page preparation before screenshot capture
 * to minimize false positives from animation timing, lazy loading, and
 * dynamic content.
 */
import type { Page } from '@playwright/test'

/**
 * Wait for the page to reach a visually stable state before taking a screenshot.
 *
 * Steps:
 * 1. Wait for network to be idle (no pending fetches)
 * 2. Wait for all images to finish loading
 * 3. Wait for CSS transitions/animations to settle
 * 4. Pause briefly for any remaining paint operations
 */
export async function waitForVisualStability(page: Page): Promise<void> {
  // Wait for network idle — all API calls and asset loads should complete
  await page.waitForLoadState('networkidle')

  // Wait for all images to be loaded to prevent blank image placeholders
  await page.evaluate(async () => {
    const images = Array.from(document.querySelectorAll('img'))
    await Promise.all(
      images
        .filter((img) => !img.complete)
        .map(
          (img) =>
            new Promise<void>((resolve) => {
              img.addEventListener('load', () => resolve())
              img.addEventListener('error', () => resolve())
            }),
        ),
    )
  })

  // Brief pause for paint stabilization after all resources loaded.
  // This addresses sub-frame rendering differences that can cause
  // spurious diffs when a screenshot captures mid-paint.
  await page.waitForTimeout(300)
}

/**
 * Hide dynamic content that changes between runs (timestamps, random IDs, etc.)
 * to prevent false-positive screenshot diffs.
 */
export async function hideDynamicContent(page: Page): Promise<void> {
  await page.evaluate(() => {
    const style = document.createElement('style')
    style.setAttribute('data-visual-test', 'true')
    style.textContent = `
      /* Hide elements that contain timestamps or relative time */
      [data-testid="timestamp"],
      [data-testid="relative-time"],
      time {
        visibility: hidden !important;
      }

      /* Freeze blinking cursors */
      * {
        caret-color: transparent !important;
      }

      /* Disable all animations and transitions for screenshot stability */
      *, *::before, *::after {
        animation-duration: 0s !important;
        animation-delay: 0s !important;
        transition-duration: 0s !important;
        transition-delay: 0s !important;
      }

      /* Hide scrollbars which may differ across platforms */
      ::-webkit-scrollbar {
        display: none !important;
      }
      * {
        scrollbar-width: none !important;
      }
    `
    document.head.appendChild(style)
  })
}

/**
 * Standard preparation sequence before every visual snapshot.
 * Call this after navigating to the target page and before toHaveScreenshot().
 */
export async function prepareForScreenshot(page: Page): Promise<void> {
  await waitForVisualStability(page)
  await hideDynamicContent(page)
}
