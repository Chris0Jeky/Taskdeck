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

  // Wait for web fonts to finish loading. Without this the first render after
  // a cold cache can paint with the fallback typeface before the webfont
  // swaps in, producing baseline/actual drift on otherwise-identical UI.
  await page.evaluate(async () => {
    if (typeof document !== 'undefined' && 'fonts' in document && document.fonts) {
      await document.fonts.ready
    }
  })

  // Brief pause for paint stabilization after all resources loaded.
  // This addresses sub-frame rendering differences that can cause
  // spurious diffs when a screenshot captures mid-paint.
  await page.waitForTimeout(300)
}

/**
 * Hide dynamic content that changes between runs to prevent false-positive
 * screenshot diffs. Also applies global animation/transition suppression
 * and hides platform-specific scrollbars.
 *
 * Note: The timestamp selectors below ([data-testid="timestamp"], time, etc.)
 * catch elements already annotated in the Vue components (CardModal and
 * ColumnEditModal metadata blocks as of TST-59). When adding new visual
 * coverage for populated views, add data-testid="timestamp" to any element
 * that renders relative or absolute times so this helper can neutralise the
 * drift automatically.
 */
export async function hideDynamicContent(page: Page): Promise<void> {
  await page.evaluate(() => {
    const style = document.createElement('style')
    style.setAttribute('data-visual-test', 'true')
    style.textContent = `
      /* Hide elements that contain timestamps or relative time (forward-looking) */
      [data-testid="timestamp"],
      [data-testid="relative-time"],
      time,

      /* Session identity and presence are per-run values, not visual contract. */
      .td-topbar__user,
      [data-presence-user],

      /* The session warning is time-dependent and must not enter a baseline. */
      [role="alert"][aria-live="assertive"],

      /*
       * Toasts are transient: the store removes each one after its own
       * duration, so how many are still on screen at capture time depends on
       * how fast the runner got there. The rule above only ever caught error
       * toasts, which are the only ones ToastContainer/PaperToastContainer
       * mark role="alert" aria-live="assertive"; success toasts raised by test
       * seeding (board/column/card "created successfully") were left visible
       * and rendered a runner-speed-dependent stack over the top-right of the
       * page. Both skins tag every toast with data-toast-id, so this hides the
       * whole stack in either. The stack is position: fixed and
       * pointer-events: none, so hiding it shifts no page layout, and because
       * this is a stylesheet rather than a one-shot DOM edit it also covers
       * toasts raised after this helper runs.
       */
      [data-toast-id] {
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
