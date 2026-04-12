/**
 * Visual regression tests for the command palette.
 *
 * Captures the command palette in its open state with the default
 * command list visible. The palette is triggered via keyboard shortcut
 * (Ctrl+K / Cmd+K).
 */
import { expect, test } from '@playwright/test'
import { registerAndAttachSession } from '../e2e/support/authSession'
import { prepareForScreenshot } from './visual-test-helpers'

test.beforeEach(async ({ page, request }) => {
  await registerAndAttachSession(page, request, 'visual-palette')
})

test('command palette open state', async ({ page }) => {
  await page.goto('/workspace/home')
  await expect(page.getByRole('heading', { name: 'Home', exact: true })).toBeVisible()

  // Open command palette via keyboard shortcut
  await page.keyboard.press('Control+k')

  // Wait for the palette to be visible (search input)
  const paletteInput = page.getByPlaceholder('Type a command or search boards and cards...')
  await expect(paletteInput).toBeVisible()

  await prepareForScreenshot(page)

  await expect(page).toHaveScreenshot('command-palette-open')
})

test('command palette with search results', async ({ page }) => {
  await page.goto('/workspace/home')
  await expect(page.getByRole('heading', { name: 'Home', exact: true })).toBeVisible()

  // Open command palette
  await page.keyboard.press('Control+k')

  const paletteInput = page.getByPlaceholder('Type a command or search boards and cards...')
  await expect(paletteInput).toBeVisible()

  // Type a search query to filter commands
  await paletteInput.fill('board')

  await prepareForScreenshot(page)

  await expect(page).toHaveScreenshot('command-palette-search')
})
