import { expect, test } from '@playwright/test'
import { registerAndAttachSession } from './support/authSession'

test.beforeEach(async ({ page, request }) => {
  await registerAndAttachSession(page, request, 'today-onboarding')
})

test('today agenda should create a useful board from setup flow', async ({ page }) => {
  const boardName = `Today Setup ${Date.now()}`

  await page.goto('/workspace/today')
  await expect(page.getByRole('heading', { name: 'Today', exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Start Useful Board' }).click()
  const setupDialog = page.getByRole('dialog', { name: 'Workspace setup' })
  await expect(setupDialog).toBeVisible()

  await setupDialog.getByPlaceholder('For example: Product Sprint').fill(boardName)
  await setupDialog.getByRole('radio', { name: /Engineering sprint/i }).check()
  await setupDialog.getByRole('button', { name: 'Create Board' }).click()

  await expect(page).toHaveURL(/\/workspace\/boards\/[a-f0-9-]+$/)
  await expect(page.getByRole('heading', { name: boardName })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Backlog', exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Review', exact: true })).toBeVisible()
})

test('today onboarding should dismiss and replay persistently', async ({ page }) => {
  await page.goto('/workspace/today')
  await expect(page.getByText('Onboarding loop')).toBeVisible()

  const dismissResponse = page.waitForResponse((response) =>
    response.request().method() === 'PUT'
    && /\/api\/workspace\/onboarding$/i.test(response.url())
    && response.ok())
  await page.getByRole('button', { name: 'Dismiss' }).click()
  await dismissResponse
  await expect(page.getByText('Setup is dismissed.')).toBeVisible()

  await page.reload()
  await expect(page.getByRole('button', { name: 'Replay Setup' })).toBeVisible()

  const replayResponse = page.waitForResponse((response) =>
    response.request().method() === 'PUT'
    && /\/api\/workspace\/onboarding$/i.test(response.url())
    && response.ok())
  await page.getByRole('button', { name: 'Replay Setup' }).click()
  await replayResponse
  await expect(page.locator('.td-today-step__title', { hasText: 'Create your first board' })).toBeVisible()
})
