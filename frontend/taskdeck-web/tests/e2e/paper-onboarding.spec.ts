import { expect, test } from '@playwright/test'

test('fresh Paper user can register and create a first board through guided setup', async ({ page }, testInfo) => {
  await page.addInitScript(() => {
    window.localStorage.setItem('td.paper.mode.v2', 'paper')
  })

  const unique = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`
  const username = `e2e-paper-onboarding-${unique}`
  const boardName = `Paper First Board ${Date.now()}`

  await page.goto('/login')
  await expect(page.getByRole('heading', { name: 'Sign in to Taskdeck' })).toBeVisible()
  await expect(page.getByText('Taskdeck · review before action')).toBeVisible()
  await page.getByRole('link', { name: 'Register' }).click()

  await expect(page).toHaveURL(/\/register$/)
  await expect(page.getByRole('heading', { name: 'Create an account' })).toBeVisible()
  await page.locator('#reg-username').fill(username)
  await page.locator('#reg-email').fill(`${username}@taskdeck.local`)
  await page.locator('#reg-password').fill('E2ePassword123!')
  await page.locator('#reg-confirm').fill('E2ePassword123!')
  await page.getByRole('button', { name: 'Create account' }).click()

  await expect(page).toHaveURL(/\/workspace\/home$/)
  await expect(page.getByTestId('paper-home')).toBeVisible()
  await expect(page.getByTestId('paper-home-first-board')).toBeVisible()
  await expect(page.getByTestId('paper-home-milestones')).toContainText('0/4 complete')

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
  // Creating the first board completes 1 of the 4 milestones (capture→review→apply remain).
  await expect(page.getByTestId('paper-home-milestones')).toContainText('1/4 complete')
})
