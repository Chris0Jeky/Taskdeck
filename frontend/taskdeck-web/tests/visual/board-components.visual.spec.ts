/**
 * Visual regression tests for the BoardToolbar and BoardActionRail
 * components.
 *
 * These components compose BoardView's header. By screenshotting each
 * locator in isolation we get targeted baselines that fail loudly on
 * structural changes to either component, without coupling the baseline
 * to board content (columns, cards) or presence identity.
 */
import { expect, test } from '@playwright/test'
import { registerAndAttachSession } from '../e2e/support/authSession'
import { createBoard } from './board-setup-helpers'
import { prepareForScreenshot } from './visual-test-helpers'

test.beforeEach(async ({ page, request }) => {
  await registerAndAttachSession(page, request, 'visual-board-components')
})

test('board toolbar default', async ({ page }) => {
  await createBoard(page, 'Visual Board Components')

  const toolbar = page.locator('.td-board-toolbar').first()
  await expect(toolbar).toBeVisible()

  await prepareForScreenshot(page)

  // Mask the presence chip — it shows the freshly-registered username,
  // which differs per run.
  await expect(toolbar).toHaveScreenshot('board-toolbar', {
    mask: [page.locator('[data-presence-user]')],
  })
})

test('board action rail default', async ({ page }) => {
  await createBoard(page, 'Visual Board Components')

  const rail = page.locator('[data-board-action-rail]').first()
  await expect(rail).toBeVisible()

  await prepareForScreenshot(page)

  await expect(rail).toHaveScreenshot('board-action-rail')
})
