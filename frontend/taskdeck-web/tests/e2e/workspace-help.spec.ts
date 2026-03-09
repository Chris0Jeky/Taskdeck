import { expect, test } from '@playwright/test'
import { registerAndAttachSession } from './support/authSession'

test.beforeEach(async ({ page, request }) => {
  await registerAndAttachSession(page, request, 'workspace-help')
})

test('workspace help should dismiss and replay across home, today, review, inbox, and selector routes', async ({ page }) => {
  await page.goto('/workspace/home')

  const homeHelp = page.locator('[data-help-topic="home"]')
  await expect(homeHelp).toContainText('What is Home for?')
  await homeHelp.getByRole('button', { name: 'Hide this guide' }).click()
  await expect(homeHelp).toContainText('This page guide is hidden.')

  await page.reload()
  await expect(homeHelp).toContainText('This page guide is hidden.')
  await homeHelp.getByRole('button', { name: 'Show page guide' }).click()
  await expect(homeHelp).toContainText('What is Home for?')

  await page.goto('/workspace/today')
  await expect(page.locator('[data-help-topic="today"]')).toContainText('What is Today for?')

  await page.goto('/workspace/review')
  await expect(page.locator('[data-help-topic="review"]')).toContainText('What is Review for?')

  await page.goto('/workspace/inbox')
  await expect(page.locator('[data-help-topic="inbox"]')).toContainText('What is Inbox for?')

  await page.goto('/workspace/activity')
  const activityHelp = page.locator('[data-help-topic="activity-selectors"]')
  await expect(activityHelp).toContainText('Why do these selectors matter?')
  await activityHelp.getByRole('button', { name: 'Hide this guide' }).click()
  await expect(activityHelp).toContainText('This page guide is hidden.')

  await page.goto('/workspace/settings/access')
  await expect(page.locator('[data-help-topic="board-access-selectors"]')).toContainText('Why use the board selector here?')
})
