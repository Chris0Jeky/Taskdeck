import { expect, test, type Page } from '@playwright/test'
import { createBoardWithColumn } from './support/boardHelpers'
import { registerAndAttachSession } from './support/authSession'
import { createCaptureItem } from './support/captureFlow'

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

  test('@mobile Paper Activity stays within the viewport', async ({ page, request }) => {
    await enablePaperMode(page)
    await registerAndAttachSession(page, request, 'paper-activity-mobile')

    await page.goto('/workspace/activity')
    await expect(page.getByRole('heading', { name: 'Activity', exact: true })).toBeVisible()

    const geometry = await page.evaluate(() => ({
      viewportWidth: window.innerWidth,
      overflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
    }))
    expect([390, 412]).toContain(geometry.viewportWidth)
    expect(geometry.overflow).toBeLessThanOrEqual(0)
  })

  test('375px phone keeps every capture action visible and keyboard reachable', async ({ page, request }) => {
    await page.setViewportSize({ width: 375, height: 812 })
    await enablePaperMode(page)
    const auth = await registerAndAttachSession(page, request, 'paper-inbox-actions')
    const seed = `${Date.now()}`
    const boardId = await createBoardWithColumn(request, auth, seed, {
      boardNamePrefix: 'Paper Inbox Actions',
      description: 'paper inbox narrow action rail',
      columnNamePrefix: 'Inbox Lane',
    })
    await createCaptureItem(request, auth, boardId, `Narrow capture ${seed}`)

    await page.goto(`/workspace/inbox?boardId=${boardId}`)
    const row = page.locator('.paper-triage__row').filter({ hasText: `Narrow capture ${seed}` })
    await expect(row).toBeVisible()
    const actions = row.locator('.paper-triage__actions button')
    await expect(actions).toHaveCount(4)

    for (const action of await actions.all()) {
      await expect(action).toBeVisible()
      const box = await action.boundingBox()
      expect(box).not.toBeNull()
      expect(box!.x).toBeGreaterThanOrEqual(0)
      expect(box!.x + box!.width).toBeLessThanOrEqual(375)
      await action.focus()
      await expect(action).toBeFocused()
    }

    const overflow = await page.evaluate(() =>
      document.documentElement.scrollWidth - document.documentElement.clientWidth,
    )
    expect(overflow).toBeLessThanOrEqual(0)
  })
})
