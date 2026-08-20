import { expect, test, type Page } from '@playwright/test'
import { registerAndAttachSession } from './support/authSession'
import { addCard, addColumn, createBoard } from './support/boardUiHelpers'

/**
 * Mobile-responsive E2E tests.
 *
 * These tests run only on mobile viewport projects (Pixel 7, iPhone 14)
 * and validate that critical workflows remain usable at small screen sizes.
 *
 * Tag: @mobile — filtered by project grep in playwright.config.ts.
 */

test.beforeEach(async ({ page, request }) => {
  await registerAndAttachSession(page, request, 'mobile', { theme: 'legacy' })
})

async function openMobileNavigation(page: Page) {
  const menuButton = page.getByRole('button', { name: 'Open navigation menu' })
  await expect(menuButton).toBeVisible()
  await menuButton.click()

  const navigation = page.getByRole('navigation', { name: 'Main navigation' })
  await expect(navigation).toBeVisible()
  return navigation
}

async function navigateWithMobileMenu(
  page: Page,
  destination: 'Boards' | 'Inbox',
  urlPattern: RegExp,
) {
  const navigation = await openMobileNavigation(page)
  const href =
    destination === 'Boards'
      ? '/workspace/boards'
      : '/workspace/inbox'
  await navigation.locator(`a[href="${href}"]`).click()
  await expect(page).toHaveURL(urlPattern)
}

function captureLauncher(page: Page) {
  return page
    .getByRole('button', { name: 'Open capture modal to add a new inbox item' })
    .first()
}

async function installSyntheticVisualViewport(page: Page) {
  await page.addInitScript(() => {
    const events = new EventTarget()
    let height = window.innerHeight
    let offsetTop = 0

    const visualViewport = {
      get height() {
        return height
      },
      get offsetTop() {
        return offsetTop
      },
      addEventListener: events.addEventListener.bind(events),
      removeEventListener: events.removeEventListener.bind(events),
    }

    Object.defineProperty(window, 'visualViewport', {
      configurable: true,
      value: visualViewport,
    })
    Object.defineProperty(window, '__taskdeckSetVisualViewport', {
      configurable: true,
      value: (next: { height: number; offsetTop: number }) => {
        height = next.height
        offsetTop = next.offsetTop
        events.dispatchEvent(new Event('resize'))
        events.dispatchEvent(new Event('scroll'))
      },
    })
  })
}

async function contractSyntheticVisualViewport(page: Page, height: number, offsetTop: number) {
  await page.evaluate(({ height: nextHeight, offsetTop: nextOffsetTop }) => {
    const setter = (window as Window & {
      __taskdeckSetVisualViewport?: (next: { height: number; offsetTop: number }) => void
    }).__taskdeckSetVisualViewport
    if (!setter) throw new Error('Synthetic visualViewport setter was not installed')
    setter({ height: nextHeight, offsetTop: nextOffsetTop })
  }, { height, offsetTop })
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

test('@mobile board navigation and column visibility on small screen', async ({ page }) => {
  const boardName = `Mobile Board ${Date.now()}`
  const columnName = boardName

  await createBoard(page, boardName)
  const columnLane = await addColumn(page, columnName)

  // The board and column deliberately share a name to prove heading assertions stay scoped.
  const boardHeading = page.getByRole('heading', { level: 1, name: boardName, exact: true })
  await expect(boardHeading).toHaveCount(1)
  await expect(boardHeading).toBeVisible()

  // Column heading should be visible and not clipped outside viewport
  const columnHeading = columnLane.getByRole('heading', { name: columnName, exact: true })
  await expect(columnHeading).toHaveCount(1)
  await expect(columnHeading).toBeVisible()

  // The viewport should be small (confirming mobile project is active)
  const viewportSize = page.viewportSize()
  expect(viewportSize).not.toBeNull()
  expect(viewportSize!.width).toBeLessThan(500)

  // Board controls (New Board, Add Column) should still be reachable
  await expect(page.getByRole('button', { name: '+ Add Column' })).toBeVisible()
})

test('@mobile card editing modal should fit within mobile viewport', async ({ page }) => {
  const boardName = `Mobile Edit Board ${Date.now()}`
  const columnName = `Mobile Edit Col ${Date.now()}`
  const cardTitle = `Mobile Edit Card ${Date.now()}`

  await createBoard(page, boardName)
  const columnLane = await addColumn(page, columnName)
  await addCard(page, columnName, cardTitle)

  // Require one lane and one card before clicking the title area.
  await expect(columnLane).toHaveCount(1)
  const card = columnLane.locator('[data-card-id]').filter({
    has: page.getByRole('heading', { name: cardTitle, exact: true }),
  })
  await expect(card).toHaveCount(1)
  const cardHeading = card.getByRole('heading', { name: cardTitle, exact: true })
  await expect(cardHeading).toHaveCount(1)
  await cardHeading.click()

  const editHeading = page.getByRole('heading', { name: 'Edit Card', exact: true })
  await expect(editHeading).toBeVisible()

  // The edit modal should be within the viewport bounds
  const viewportSize = page.viewportSize()
  expect(viewportSize).not.toBeNull()

  const modal = page.getByRole('dialog', { name: 'Edit Card' })
  await expect(modal).toBeVisible()
  const modalBox = await modal.boundingBox()
  expect(modalBox).not.toBeNull()
  // Modal should not exceed viewport width
  expect(modalBox!.x + modalBox!.width).toBeLessThanOrEqual(viewportSize!.width + 2)
  // Modal should have a reasonable minimum width on mobile
  expect(modalBox!.width).toBeGreaterThan(200)

  // Close the modal
  await page.keyboard.press('Escape')
  await expect(editHeading).not.toBeVisible()
})

test('@mobile card editing modal follows a contracted visual viewport', async ({ page }) => {
  await installSyntheticVisualViewport(page)

  const boardName = `Visual Viewport Board ${Date.now()}`
  const columnName = `Visual Viewport Col ${Date.now()}`
  const cardTitle = `Visual Viewport Card ${Date.now()}`

  await createBoard(page, boardName)
  const columnLane = await addColumn(page, columnName)
  await addCard(page, columnName, cardTitle)

  const card = columnLane.locator('[data-card-id]').filter({
    has: page.getByRole('heading', { name: cardTitle, exact: true }),
  })
  await card.getByRole('heading', { name: cardTitle, exact: true }).click()

  const editModal = page.getByRole('dialog', { name: 'Edit Card' })
  const scrollRegion = page.getByTestId('card-modal-scroll-region')
  await expect(editModal).toBeVisible()

  const layoutViewportHeight = await page.evaluate(() => window.innerHeight)
  await contractSyntheticVisualViewport(page, 420, 120)

  await expect.poll(async () => {
    const box = await editModal.boundingBox()
    return box ? { y: Math.round(box.y), height: Math.round(box.height) } : null
  }).toEqual({ y: 120, height: 420 })

  const viewportState = await page.evaluate(() => ({
    layoutHeight: window.innerHeight,
    visualHeight: window.visualViewport?.height,
    visualOffsetTop: window.visualViewport?.offsetTop,
  }))
  expect(viewportState.layoutHeight).toBe(layoutViewportHeight)
  expect(viewportState.visualHeight).toBe(420)
  expect(viewportState.visualOffsetTop).toBe(120)

  await expect(scrollRegion).toHaveCSS('overflow-y', 'auto')
  const scrollMetrics = await scrollRegion.evaluate((element) => ({
    clientHeight: element.clientHeight,
    scrollHeight: element.scrollHeight,
  }))
  expect(scrollMetrics.scrollHeight).toBeGreaterThan(scrollMetrics.clientHeight)

  // Measure the modal's own actions against the contracted visual bounds.
  // `toBeVisible()` alone cannot carry this claim: Playwright treats any
  // element with a non-empty rendered box as visible, including one the
  // software keyboard covers.
  const visualTop = 120
  const visualBottom = visualTop + 420
  for (const name of ['Save Changes', 'Cancel', 'Delete Card']) {
    const action = editModal.getByRole('button', { name, exact: true })
    await action.scrollIntoViewIfNeeded()
    await expect(action).toBeVisible()
    await expect(action).toBeEnabled()

    const actionBox = await action.boundingBox()
    expect(actionBox).not.toBeNull()
    // 1px tolerance for sub-pixel layout rounding.
    expect(actionBox!.y).toBeGreaterThanOrEqual(visualTop - 1)
    expect(actionBox!.y + actionBox!.height).toBeLessThanOrEqual(visualBottom + 1)
  }

  // Nested Delete Card confirmation — a `TdDialog`, the shared primitive behind
  // every confirmation in the product. It teleports to <body>, so nothing in
  // CardModal's tree can constrain it; #1821 bound it to the visual viewport in
  // its own right and this block is that binding's keyboard-safety coverage.
  await editModal.getByRole('button', { name: 'Delete Card', exact: true }).click()
  const deleteDialog = page.getByRole('dialog', { name: 'Delete Card', exact: true })
  await expect(deleteDialog).toBeVisible()

  // The dialog's own box must match the CONTRACTED visual bounds, not the
  // layout viewport it used to span.
  await expect.poll(async () => {
    const box = await deleteDialog.boundingBox()
    return box ? { y: Math.round(box.y), height: Math.round(box.height) } : null
  }).toEqual({ y: visualTop, height: 420 })

  const deleteDialogBox = await deleteDialog.boundingBox()
  expect(deleteDialogBox).not.toBeNull()
  expect(deleteDialogBox!.height).toBeLessThan(layoutViewportHeight)

  // Footer actions measured against the contracted bounds. `toBeVisible()` /
  // `toBeFocused()` cannot carry this claim: Playwright treats any element with
  // a non-empty rendered box as visible, and focus is not a screen position —
  // both pass for a control sitting under the software keyboard.
  for (const name of ['Cancel', 'Delete']) {
    const action = deleteDialog.getByRole('button', { name, exact: true })
    await action.scrollIntoViewIfNeeded()
    await expect(action).toBeEnabled()

    const actionBox = await action.boundingBox()
    expect(actionBox).not.toBeNull()
    // 1px tolerance for sub-pixel layout rounding.
    expect(actionBox!.y).toBeGreaterThanOrEqual(visualTop - 1)
    expect(actionBox!.y + actionBox!.height).toBeLessThanOrEqual(visualBottom + 1)
  }
})

test('@mobile confirmation dialog spans the full sheet without a contracted visual viewport', async ({
  page,
}) => {
  // Deliberately NO synthetic viewport: this is the regression guard for the
  // ordinary case, where the browser's real visual viewport still matches the
  // layout viewport and the mobile sheet must keep spanning it.
  const boardName = `Dialog Baseline Board ${Date.now()}`
  const columnName = `Dialog Baseline Col ${Date.now()}`
  const cardTitle = `Dialog Baseline Card ${Date.now()}`

  await createBoard(page, boardName)
  const columnLane = await addColumn(page, columnName)
  await addCard(page, columnName, cardTitle)

  const card = columnLane.locator('[data-card-id]').filter({
    has: page.getByRole('heading', { name: cardTitle, exact: true }),
  })
  await card.getByRole('heading', { name: cardTitle, exact: true }).click()

  const editModal = page.getByRole('dialog', { name: 'Edit Card' })
  await expect(editModal).toBeVisible()

  // CardModal's own body scrolls inside a clipped container, so the trigger has
  // to be scrolled into that container before it is clickable.
  const deleteTrigger = editModal.getByRole('button', { name: 'Delete Card', exact: true })
  await deleteTrigger.scrollIntoViewIfNeeded()
  await deleteTrigger.click()
  const deleteDialog = page.getByRole('dialog', { name: 'Delete Card', exact: true })
  await expect(deleteDialog).toBeVisible()

  // Measure the dialog and the viewport in ONE evaluation: reading them in two
  // round-trips lets a scroll settle in between and compares skewed samples.
  const measure = () =>
    page.evaluate(() => {
      const dialog = document.querySelector('[role="dialog"][aria-label="Delete Card"]')
      if (!dialog) return null
      const rect = dialog.getBoundingClientRect()
      const visual = window.visualViewport
      const visualHeight = visual?.height ?? window.innerHeight
      const visualOffsetTop = visual?.offsetTop ?? 0
      const footer = Array.from(dialog.querySelectorAll('button'))
        .filter((button) => ['Cancel', 'Delete'].includes(button.textContent?.trim() ?? ''))
        .map((button) => {
          const buttonRect = button.getBoundingClientRect()
          return { top: buttonRect.top, bottom: buttonRect.bottom }
        })
      return {
        layoutHeight: window.innerHeight,
        visualHeight,
        visualOffsetTop,
        // 1px tolerance for sub-pixel layout rounding, expressed as booleans so
        // a poll can wait for the geometry to settle.
        //
        // WebKit's iPhone emulation reports a visual viewport a few pixels off
        // `window.innerHeight` even with nothing contracting it, so "still a
        // full sheet" is a ratio, not an equality.
        spansMostOfLayoutViewport: rect.height >= window.innerHeight * 0.9,
        topMatchesVisualTop: Math.abs(rect.top - visualOffsetTop) <= 1,
        heightMatchesVisualHeight: Math.abs(rect.height - visualHeight) <= 1,
        footerCount: footer.length,
        footerInsideVisualBounds: footer.every(
          (entry) =>
            entry.top >= visualOffsetTop - 1 && entry.bottom <= visualOffsetTop + visualHeight + 1,
        ),
      }
    })

  await expect
    .poll(async () => {
      const measured = await measure()
      return measured
        ? {
            spansMostOfLayoutViewport: measured.spansMostOfLayoutViewport,
            topMatchesVisualTop: measured.topMatchesVisualTop,
            heightMatchesVisualHeight: measured.heightMatchesVisualHeight,
            footerCount: measured.footerCount,
            footerInsideVisualBounds: measured.footerInsideVisualBounds,
          }
        : null
    })
    .toEqual({
      // Nothing contracted this viewport, so the sheet must still span it top to
      // bottom — the `100dvh` behaviour #1821 must not regress.
      spansMostOfLayoutViewport: true,
      topMatchesVisualTop: true,
      heightMatchesVisualHeight: true,
      footerCount: 2,
      footerInsideVisualBounds: true,
    })
})

test('@mobile workspace views should render correctly on small screen', async ({ page }) => {
  await page.goto('/workspace/home')
  await expect(page).toHaveURL(/\/workspace\/home$/)

  const viewportSize = page.viewportSize()
  expect(viewportSize).not.toBeNull()
  expect(viewportSize!.width).toBeLessThan(500)

  // Home heading should be visible
  await expect(page.getByRole('heading', { name: 'Home', exact: true })).toBeVisible()

  // Navigate using the mobile hamburger menu rather than bypassing the UI.
  await navigateWithMobileMenu(page, 'Boards', /\/workspace\/boards$/)
  await expect(page.getByRole('button', { name: '+ New Board' })).toBeVisible()

  await navigateWithMobileMenu(page, 'Inbox', /\/workspace\/inbox$/)
  await expect(captureLauncher(page)).toBeVisible()

  // Each workspace view should render its primary content within viewport
  const body = page.locator('body')
  const bodyBox = await body.boundingBox()
  expect(bodyBox).not.toBeNull()
  // Body should not be wider than the viewport (no horizontal overflow forcing scroll)
  // Allow small tolerance for scrollbar
  expect(bodyBox!.width).toBeLessThanOrEqual(viewportSize!.width + 20)
})

test('@mobile board columns stack vertically without horizontal overflow', async ({ page }) => {
  // FE-19: On mobile the board must switch from a horizontal kanban to a
  // vertical card list so core navigation remains usable at ~375-412px.
  const boardName = `Stack Board ${Date.now()}`
  const firstColumn = `Backlog ${Date.now()}`
  const secondColumn = `Doing ${Date.now()}`

  await createBoard(page, boardName)
  const firstLane = await addColumn(page, firstColumn)
  const secondLane = await addColumn(page, secondColumn)

  await expect(firstLane).toHaveCount(1)
  await expect(secondLane).toHaveCount(1)

  const firstBox = await firstLane.boundingBox()
  const secondBox = await secondLane.boundingBox()
  expect(firstBox).not.toBeNull()
  expect(secondBox).not.toBeNull()

  // Vertical stack: the second column sits below the first, not beside it.
  expect(secondBox!.y).toBeGreaterThanOrEqual(firstBox!.y + firstBox!.height - 4)

  // Each lane must fit entirely inside the viewport — no horizontal scroll.
  const viewportSize = page.viewportSize()
  expect(viewportSize).not.toBeNull()
  expect(firstBox!.x + firstBox!.width).toBeLessThanOrEqual(viewportSize!.width + 2)
  expect(secondBox!.x + secondBox!.width).toBeLessThanOrEqual(viewportSize!.width + 2)

  // Document scroll width must not exceed viewport — confirms no horizontal overflow.
  const scrollOverflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  )
  expect(scrollOverflow).toBeLessThanOrEqual(2)
})

test('@mobile capture modal should be usable on small screen', async ({ page }) => {
  await page.goto('/workspace/home')
  await expect(page.getByRole('heading', { name: 'Home', exact: true })).toBeVisible()
  await navigateWithMobileMenu(page, 'Inbox', /\/workspace\/inbox$/)

  const captureText = `Mobile capture ${Date.now()}`

  await captureLauncher(page).click()
  const captureModal = page.getByRole('dialog', { name: 'Capture item' })
  await expect(captureModal).toBeVisible()

  // The capture textarea should be visible and interactable
  const captureInput = captureModal.getByPlaceholder('Capture a thought, task, or follow-up...')
  await expect(captureInput).toBeVisible()

  // On mobile the modal should fit the viewport
  const viewportSize = page.viewportSize()
  expect(viewportSize).not.toBeNull()

  const modalBox = await captureModal.boundingBox()
  if (modalBox) {
    expect(modalBox.x + modalBox.width).toBeLessThanOrEqual(viewportSize!.width + 2)
  }

  // Type and submit through the actual mobile-visible action button.
  await captureInput.fill(captureText)
  await captureModal.getByRole('button', { name: 'Save Capture' }).click()

  // Inbox should stay visible and show the newly created capture.
  await expect(page).toHaveURL(/\/workspace\/inbox$/)
  await expect(page.locator('.td-inbox-row__excerpt').first()).toContainText(captureText)
})
