import { expect, test, type Page } from '@playwright/test'
import { createBoardWithColumn } from './support/boardHelpers'
import { API_BASE_URL, registerAndAttachSession } from './support/authSession'
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

    const more = page.getByRole('button', { name: 'More', exact: true })
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

  test('Wide Paper lanes let cards use the full lane content width', async ({ page, request }) => {
    await page.setViewportSize({ width: 1280, height: 844 })
    await enablePaperMode(page)
    const auth = await registerAndAttachSession(page, request, 'paper-wide-card')
    const seed = `${Date.now()}`
    const boardId = await createBoardWithColumn(request, auth, seed, {
      boardNamePrefix: 'Paper Wide Card',
      description: 'Synthetic wide-card layout regression fixture.',
      columnNamePrefix: 'Wide Lane',
    })
    const boardResponse = await request.get(`${API_BASE_URL}/boards/${boardId}`, {
      headers: { Authorization: `Bearer ${auth.token}` },
    })
    expect(boardResponse.ok()).toBe(true)
    const board = await boardResponse.json() as { columns: Array<{ id: string }> }
    const createCardResponse = await request.post(`${API_BASE_URL}/boards/${boardId}/cards`, {
      headers: { Authorization: `Bearer ${auth.token}` },
      data: {
        columnId: board.columns[0]!.id,
        title: `Wide card ${seed}`,
        description: 'Synthetic-only layout fixture.',
        dueDate: null,
        labelIds: [],
      },
    })
    expect(createCardResponse.ok()).toBe(true)

    await page.goto(`/workspace/boards/${boardId}`)
    await page.getByLabel('Column width').selectOption('wide')
    const geometry = await page.locator('.paper-board-column__cards').evaluate((cards) => {
      const card = cards.querySelector('.paper-board-card')
      if (!(card instanceof HTMLElement)) throw new Error('Synthetic card did not render')
      return {
        cardsWidth: cards.getBoundingClientRect().width,
        cardWidth: card.getBoundingClientRect().width,
      }
    })

    expect(geometry.cardWidth).toBeCloseTo(geometry.cardsWidth, 1)
  })

  test('Paper card inspector moves only outside focus into the phone modal', async ({ page, request }, testInfo) => {
    await page.setViewportSize({ width: 1280, height: 844 })
    await enablePaperMode(page)
    const auth = await registerAndAttachSession(page, request, 'paper-im-focus')
    const seed = `${Date.now()}`
    const boardId = await createBoardWithColumn(request, auth, seed, {
      boardNamePrefix: 'Paper inspector focus',
      description: 'Synthetic inspector-to-modal focus regression fixture.',
      columnNamePrefix: 'Focus lane',
    })
    const boardResponse = await request.get(`${API_BASE_URL}/boards/${boardId}`, {
      headers: { Authorization: `Bearer ${auth.token}` },
    })
    expect(boardResponse.ok()).toBe(true)
    const board = await boardResponse.json() as { columns: Array<{ id: string }> }
    const cardTitle = `Focus card ${seed}`
    const createCardResponse = await request.post(`${API_BASE_URL}/boards/${boardId}/cards`, {
      headers: { Authorization: `Bearer ${auth.token}` },
      data: {
        columnId: board.columns[0]!.id,
        title: cardTitle,
        description: 'Synthetic-only focus transition fixture.',
        dueDate: null,
        labelIds: [],
      },
    })
    expect(createCardResponse.ok()).toBe(true)

    await page.goto(`/workspace/boards/${boardId}`)
    const opener = page.getByRole('button', { name: new RegExp(cardTitle) })
    const editor = page.getByRole('dialog', { name: 'Edit Card' })
    const closeEditor = editor.getByRole('button', { name: 'Close card editor' })
    await opener.focus()
    await page.keyboard.press('Enter')
    await expect(editor).toBeVisible()
    await expect(editor).not.toHaveAttribute('aria-modal', 'true')

    // A desktop inspector is non-modal, so a board control can still own focus.
    // Crossing into the phone modal moves that outside focus to CardModal's
    // close control, which is contained by the newly modal editor.
    const densityToggle = page.getByTestId('paper-board-density-toggle')
    await densityToggle.focus()
    await expect(densityToggle).toBeFocused()
    await page.setViewportSize({ width: 390, height: 844 })
    await expect(editor).toHaveAttribute('aria-modal', 'true')
    await expect(closeEditor).toBeFocused()
    await expect.poll(() => editor.evaluate((element) => element.contains(document.activeElement))).toBe(true)
    await page.screenshot({ path: testInfo.outputPath('paper-inspector-to-modal-focus.png') })
    await closeEditor.click()
    await expect(opener).toBeFocused()

    // Focus already inside the editor is deliberate editing context and must
    // survive the same presentation transition.
    await page.setViewportSize({ width: 1280, height: 844 })
    await opener.focus()
    await page.keyboard.press('Enter')
    const title = editor.locator('#card-title')
    await title.focus()
    await expect(title).toBeFocused()
    await page.setViewportSize({ width: 390, height: 844 })
    await expect(editor).toHaveAttribute('aria-modal', 'true')
    await expect(title).toBeFocused()
    await closeEditor.click()
    await expect(opener).toBeFocused()

    // A shell modal owns its own focused control. CardModal must not take that
    // control while it changes from inspector to mobile modal underneath it.
    await page.setViewportSize({ width: 1280, height: 844 })
    await opener.focus()
    await page.keyboard.press('Enter')
    await page.keyboard.press('Control+K')
    const palette = page.getByRole('dialog', { name: 'Command palette' })
    const paletteInput = palette.getByRole('combobox', { name: 'Command palette search' })
    await expect(palette).toBeVisible()
    await expect(paletteInput).toBeFocused()
    await page.setViewportSize({ width: 390, height: 844 })
    await expect(editor).toHaveAttribute('aria-modal', 'true')
    await expect(paletteInput).toBeFocused()
    await page.keyboard.press('Escape')
    await expect(palette).toHaveCount(0)
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
