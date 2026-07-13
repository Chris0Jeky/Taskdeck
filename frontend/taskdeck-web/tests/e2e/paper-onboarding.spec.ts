import { expect, test } from '@playwright/test'
import { registerAndAttachSession } from './support/authSession'

test('fresh Paper user can create a first board through guided setup', async ({ page, request }, testInfo) => {
  await page.addInitScript(() => {
    window.localStorage.setItem('td.paper.mode.v2', 'paper')
  })
  await registerAndAttachSession(page, request, 'paper-onboarding')

  const boardName = `Paper First Board ${Date.now()}`

  await page.goto('/workspace/home')

  await expect(page.getByTestId('paper-home')).toBeVisible()
  await expect(page.getByTestId('paper-home-first-board')).toBeVisible()
  await expect(page.getByTestId('paper-home-milestones')).toContainText('0/3 complete')

  await page.getByTestId('paper-home-setup-cta').click()
  const setupDialog = page.getByRole('dialog', { name: 'Workspace setup' })
  await expect(setupDialog).toBeVisible()
  await page.screenshot({ path: testInfo.outputPath('paper-guided-setup.png') })

  await setupDialog.getByPlaceholder('For example: Product Sprint').fill(boardName)
  await setupDialog.getByRole('radio', { name: /Blank board/i }).check()
  await setupDialog.getByRole('button', { name: 'Create Board' }).click()

  await expect(page).toHaveURL(/\/workspace\/boards\/[a-f0-9-]+$/)
  await expect(page.getByRole('heading', { name: boardName })).toBeVisible()
  await page.screenshot({ path: testInfo.outputPath('paper-first-board.png') })

  await page.goto('/workspace/home')
  await expect(page.getByTestId('paper-home')).toBeVisible()
  await expect(page.getByTestId('paper-home-first-board')).toHaveCount(0)
  await expect(page.getByTestId('paper-home-milestones')).toContainText('1/3 complete')
})
