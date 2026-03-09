import { expect, test } from '@playwright/test'
import { registerAndAttachSession } from './support/authSession'

test.beforeEach(async ({ page, request }) => {
  await registerAndAttachSession(page, request, 'workspace-help')
})

test('home help should dismiss and replay persistently', async ({ page }) => {
  await page.goto('/workspace/home')

  const homeHelp = page.locator('[data-help-topic="home"]')
  await expect(homeHelp.getByRole('heading', { name: 'What is Home for?' })).toBeVisible()

  await homeHelp.getByRole('button', { name: 'Hide this guide' }).click()
  await expect(homeHelp.getByText('This page guide is hidden.')).toBeVisible()

  await page.reload()
  await expect(homeHelp.getByRole('button', { name: 'Show page guide' })).toBeVisible()

  await homeHelp.getByRole('button', { name: 'Show page guide' }).click()
  await expect(homeHelp.getByRole('heading', { name: 'What is Home for?' })).toBeVisible()
})

test('review and inbox should expose workflow guidance', async ({ page }) => {
  await page.goto('/workspace/review')
  const reviewHelp = page.locator('[data-help-topic="review"]')
  await expect(reviewHelp.getByRole('heading', { name: 'What is Review for?' })).toBeVisible()

  await page.goto('/workspace/inbox')
  const inboxHelp = page.locator('[data-help-topic="inbox"]')
  await expect(inboxHelp.getByRole('heading', { name: 'What is Inbox for?' })).toBeVisible()
})
