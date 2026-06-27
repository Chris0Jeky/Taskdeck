import { expect, test, type Page } from '@playwright/test'
import { createBoardWithColumn } from './support/boardHelpers'
import { registerAndAttachSession } from './support/authSession'

async function enablePaperMode(page: Page) {
  await page.addInitScript(() => {
    window.localStorage.setItem('td.paper.mode.v2', 'paper')
  })
}

test.describe('Paper responsive shell', () => {
  test('375px phone uses bottom navigation and keeps board content above it', async ({ page, request }) => {
    await page.setViewportSize({ width: 375, height: 812 })
    await enablePaperMode(page)
    const auth = await registerAndAttachSession(page, request, 'paper-phone')
    const seed = `${Date.now()}`
    const boardId = await createBoardWithColumn(request, auth, seed, {
      boardNamePrefix: 'Paper Phone',
      description: 'paper phone responsive board',
      columnNamePrefix: 'Phone Lane',
    })

    await page.goto(`/workspace/boards/${boardId}`)

    await expect(page.locator('[data-paper-bottombar]')).toBeVisible()
    await expect(page.locator('.td-mobile-topbar__hamburger')).toHaveCount(0)
    await expect(page.locator('[data-testid="paper-board-lanes"]')).toBeVisible()

    const contentPaddingBottom = await page.locator('.td-content').evaluate((element) =>
      Number.parseFloat(window.getComputedStyle(element).paddingBottom),
    )
    expect(contentPaddingBottom).toBeGreaterThanOrEqual(56)

    const more = page.getByRole('button', { name: 'More' })
    await expect(more).toHaveAttribute('aria-expanded', 'false')
    await more.click()
    await expect(more).toHaveAttribute('aria-expanded', 'true')
    await expect(page.locator('[data-paper-phone-drawer]')).toBeVisible()
    await page.keyboard.press('Escape')
    await expect(page.locator('[data-paper-phone-drawer]')).toHaveCount(0)
  })

  test('768px tablet uses the icon rail and snap board lanes', async ({ page, request }) => {
    await page.setViewportSize({ width: 768, height: 1024 })
    await enablePaperMode(page)
    const auth = await registerAndAttachSession(page, request, 'paper-tablet')
    const seed = `${Date.now()}`
    const boardId = await createBoardWithColumn(request, auth, seed, {
      boardNamePrefix: 'Paper Tablet',
      description: 'paper tablet responsive board',
      columnNamePrefix: 'Tablet Lane',
    })

    await page.goto(`/workspace/boards/${boardId}`)

    await expect(page.locator('[data-paper-rail]')).toBeVisible()
    await expect(page.locator('[data-paper-bottombar]')).toHaveCount(0)
    await expect(page.getByRole('heading', { name: `Paper Tablet ${seed}` })).toBeVisible()
    await expect(page.locator('[data-testid="paper-board-lanes"]')).toHaveClass(/paper-board-view__lanes--snap/)
  })
})
