import { expect, test, type Locator, type Page } from '@playwright/test'
import { API_BASE_URL, registerAndAttachSession } from './support/authSession'
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
    // Height is LAZY on purpose. An init script runs before the page's own
    // scripts and before the viewport emulation has settled, so reading
    // `window.innerHeight` here froze whatever height the browser happened to
    // have at that instant — not the mobile project's emulated height. Report
    // the live layout viewport until a test explicitly contracts the synthetic
    // one, so an uncontracted read is never a stale pre-emulation number.
    let heightOverride: number | null = null
    let offsetTop = 0
    let scale = 1

    // Keep the engine's real VisualViewport reachable for diagnostics: the
    // synthetic object replaces `window.visualViewport`, and triaging a
    // geometry failure needs the engine's own `offsetTop` to tell a product
    // bug apart from the coordinate-space artifact
    // `measureInLayoutViewportSpace` documents below.
    const realVisualViewport = window.visualViewport

    const visualViewport = {
      get height() {
        return heightOverride ?? window.innerHeight
      },
      get offsetTop() {
        return offsetTop
      },
      get scale() {
        return scale
      },
      addEventListener: events.addEventListener.bind(events),
      removeEventListener: events.removeEventListener.bind(events),
    }

    Object.defineProperty(window, 'visualViewport', {
      configurable: true,
      value: visualViewport,
    })
    Object.defineProperty(window, '__taskdeckRealVisualViewport', {
      configurable: true,
      value: realVisualViewport,
    })
    Object.defineProperty(window, '__taskdeckSetVisualViewport', {
      configurable: true,
      value: (next: { height: number; offsetTop: number; scale?: number }) => {
        heightOverride = next.height
        offsetTop = next.offsetTop
        scale = next.scale ?? 1
        events.dispatchEvent(new Event('resize'))
        events.dispatchEvent(new Event('scroll'))
      },
    })
  })
}

/**
 * An element's box, converted back into LAYOUT viewport space — the space a
 * `position: fixed` CSS `top` is written in.
 *
 * Two coordinate spaces are in play and they are not the same space:
 *
 * - `position: fixed` resolves against the LAYOUT viewport.
 * - `getBoundingClientRect()`, and therefore Playwright's `boundingBox()`, is
 *   expressed in VISUAL viewport coordinates.
 *
 * They coincide only while `visualViewport.offsetTop` is 0, because
 * `clientTop = cssTop - visualViewport.offsetTop`.
 *
 * On Chromium the two spaces stay aligned and the origin is 0. On WebKit they
 * do not: `src/style.css` styles `::-webkit-scrollbar` with an explicit width,
 * so WebKit gives the root scroller classic space-taking scrollbars and the
 * visual viewport ends up 8px shorter than the layout viewport. Once the
 * document is scrolled, WebKit parks the visual viewport at the bottom of that
 * 8px slack, `offsetTop` becomes 8, and every fixed element measures 8px HIGHER
 * than its CSS `top`. That is the whole of issue #2180's nightly red: the
 * dialog was positioned correctly and the assertions were written in the wrong
 * space.
 *
 * The origin is MEASURED, never assumed: a `position: fixed; top: 0` sentinel
 * is read back with `getBoundingClientRect()`, so this reports whatever the
 * engine actually does, and it is deliberately not a tolerance band — an 8px
 * slop would hide a real 8px regression just as well as the artifact.
 *
 * The origin and the element's rect are read in ONE evaluation on purpose. The
 * origin is only valid for the scroll position it was taken at, and this test
 * scrolls: `scrollIntoViewIfNeeded()` before each footer action, and the click
 * that opens the nested confirmation. Nothing locks body scroll while a dialog
 * is open, so a conversion factor fetched in a separate round-trip can describe
 * a scroll position the rect no longer has.
 */
async function measureInLayoutViewportSpace(locator: Locator): Promise<{
  layoutTop: number
  layoutBottom: number
  height: number
  fixedOrigin: number
}> {
  return locator.evaluate((element) => {
    const sentinel = document.createElement('div')
    sentinel.style.position = 'fixed'
    sentinel.style.top = '0'
    sentinel.style.left = '0'
    sentinel.style.width = '1px'
    sentinel.style.height = '1px'
    sentinel.style.visibility = 'hidden'
    sentinel.style.pointerEvents = 'none'
    document.body.appendChild(sentinel)
    const fixedOrigin = sentinel.getBoundingClientRect().top
    sentinel.remove()

    const rect = element.getBoundingClientRect()
    return {
      layoutTop: rect.top - fixedOrigin,
      layoutBottom: rect.bottom - fixedOrigin,
      height: rect.height,
      fixedOrigin,
    }
  })
}

async function contractSyntheticVisualViewport(
  page: Page,
  height: number,
  offsetTop: number,
  scale = 1,
) {
  await page.evaluate(({ height: nextHeight, offsetTop: nextOffsetTop, scale: nextScale }) => {
    const setter = (window as Window & {
      __taskdeckSetVisualViewport?: (next: {
        height: number
        offsetTop: number
        scale?: number
      }) => void
    }).__taskdeckSetVisualViewport
    if (!setter) throw new Error('Synthetic visualViewport setter was not installed')
    setter({ height: nextHeight, offsetTop: nextOffsetTop, scale: nextScale })
  }, { height, offsetTop, scale })
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
  await contractSyntheticVisualViewport(page, 420, 120, 1)

  // The contracted band expressed in LAYOUT viewport space, which is the space
  // the dialog's CSS `top` is written in. Every measurement below is converted
  // back into it by `measureInLayoutViewportSpace`, which reads its conversion
  // origin in the same evaluation as the rect it corrects — so on a WebKit run
  // whose visual viewport is parked 8px down, a raw `boundingBox()` of 112
  // converts to the 120 asserted here, and no reading is reused across a scroll.
  const contractedTop = 120
  const contractedHeight = 420
  const contractedBottom = contractedTop + contractedHeight

  await expect.poll(async () => {
    const box = await measureInLayoutViewportSpace(editModal)
    return { y: Math.round(box.layoutTop), height: Math.round(box.height) }
  }).toEqual({ y: contractedTop, height: contractedHeight })

  const viewportState = await page.evaluate(() => ({
    layoutHeight: window.innerHeight,
    visualHeight: window.visualViewport?.height,
    visualOffsetTop: window.visualViewport?.offsetTop,
  }))
  expect(viewportState.layoutHeight).toBe(layoutViewportHeight)
  expect(viewportState.visualHeight).toBe(420)
  expect(viewportState.visualOffsetTop).toBe(120)

  // Policy boundary for this synthetic regression: a scale-only change is
  // pinch-zoom evidence, not a keyboard contraction. Preserve the measured
  // height/offset contract so zoom does not move modal controls, while leaving
  // native pinch transforms and physical-device reachability to device testing.
  const modalBeforeScaleOnlyChange = await measureInLayoutViewportSpace(editModal)
  await contractSyntheticVisualViewport(page, 420, 120, 2)
  const zoomedViewportState = await page.evaluate(() => ({
    visualHeight: window.visualViewport?.height,
    visualOffsetTop: window.visualViewport?.offsetTop,
    visualScale: window.visualViewport?.scale,
  }))
  expect(zoomedViewportState).toEqual({
    visualHeight: 420,
    visualOffsetTop: 120,
    visualScale: 2,
  })
  await expect.poll(async () => {
    const box = await measureInLayoutViewportSpace(editModal)
    return {
      y: Math.round(box.layoutTop),
      height: Math.round(box.height),
    }
  }).toEqual({
    y: Math.round(modalBeforeScaleOnlyChange.layoutTop),
    height: Math.round(modalBeforeScaleOnlyChange.height),
  })

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
  for (const name of ['Save Changes', 'Cancel', 'Delete Card']) {
    const action = editModal.getByRole('button', { name, exact: true })
    await action.scrollIntoViewIfNeeded()
    await expect(action).toBeVisible()
    await expect(action).toBeEnabled()

    // Measured after the scroll this loop just performed, with its own origin
    // read in the same evaluation.
    const bounds = await measureInLayoutViewportSpace(action)
    // 1px tolerance for sub-pixel layout rounding.
    expect(bounds.layoutTop).toBeGreaterThanOrEqual(contractedTop - 1)
    expect(bounds.layoutBottom).toBeLessThanOrEqual(contractedBottom + 1)
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
  // Re-measured here rather than reusing anything from above: the click that
  // opened this dialog can scroll the document.
  await expect.poll(async () => {
    const box = await measureInLayoutViewportSpace(deleteDialog)
    return { y: Math.round(box.layoutTop), height: Math.round(box.height) }
  }).toEqual({ y: contractedTop, height: contractedHeight })

  const deleteDialogBounds = await measureInLayoutViewportSpace(deleteDialog)
  // Heights are identical in both spaces, so this one needs no conversion.
  expect(deleteDialogBounds.height).toBeLessThan(layoutViewportHeight)

  // Footer actions measured against the contracted bounds. `toBeVisible()` /
  // `toBeFocused()` cannot carry this claim: Playwright treats any element with
  // a non-empty rendered box as visible, and focus is not a screen position —
  // both pass for a control sitting under the software keyboard.
  for (const name of ['Cancel', 'Delete']) {
    const action = deleteDialog.getByRole('button', { name, exact: true })
    await action.scrollIntoViewIfNeeded()
    await expect(action).toBeEnabled()

    // Measured after the scroll this loop just performed, with its own origin
    // read in the same evaluation.
    const bounds = await measureInLayoutViewportSpace(action)
    // 1px tolerance for sub-pixel layout rounding.
    expect(bounds.layoutTop).toBeGreaterThanOrEqual(contractedTop - 1)
    expect(bounds.layoutBottom).toBeLessThanOrEqual(contractedBottom + 1)
  }
})

test('@mobile confirmation dialog spans the full sheet without a contracted visual viewport', async ({
  page,
  browserName,
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

      // The layout viewport's origin in client-rect coordinates, measured the
      // same way as the module-level `measureInLayoutViewportSpace` helper and
      // inlined here on purpose: `page.evaluate` cannot close over module
      // scope, and reading the origin in a second round-trip would let a scroll
      // settle in between and compare skewed samples — the same reason the
      // dialog and the viewport are read together.
      //
      // `position: fixed` is layout-viewport space, `getBoundingClientRect()`
      // is visual-viewport space, and `clientTop = cssTop - offsetTop`. On
      // WebKit the app's classic 8px scrollbars leave the visual viewport 8px
      // short of the layout viewport, so a scrolled document parks it at
      // `offsetTop: 8` and every fixed element measures 8px higher than its
      // CSS `top`. This
      // conversion is why issue #2180's nightly red was an assertion bug and
      // not a product one.
      const sentinel = document.createElement('div')
      sentinel.style.position = 'fixed'
      sentinel.style.top = '0'
      sentinel.style.left = '0'
      sentinel.style.width = '1px'
      sentinel.style.height = '1px'
      sentinel.style.visibility = 'hidden'
      sentinel.style.pointerEvents = 'none'
      document.body.appendChild(sentinel)
      const fixedOrigin = sentinel.getBoundingClientRect().top
      sentinel.remove()

      // The contracted-or-not visual band, expressed in client-rect space.
      const visualTopInClientSpace = fixedOrigin + visualOffsetTop
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
        topMatchesVisualTop: Math.abs(rect.top - visualTopInClientSpace) <= 1,
        heightMatchesVisualHeight: Math.abs(rect.height - visualHeight) <= 1,
        // The coordinate-space artifact itself, pinned as tested behaviour
        // rather than absorbed into a tolerance. Only asserted on WebKit — see
        // `expectsNegatedOrigin` below.
        fixedOriginNegatesVisualOffsetTop: Math.abs(fixedOrigin + visualOffsetTop) <= 1,
        footerCount: footer.length,
        footerInsideVisualBounds: footer.every(
          (entry) =>
            entry.top >= visualTopInClientSpace - 1 &&
            entry.bottom <= visualTopInClientSpace + visualHeight + 1,
        ),
      }
    })

  // `fixedOrigin === -visualOffsetTop` is a WebKit convention, not a portable
  // rule, so it is asserted only there. WebKit reports client rects relative to
  // the VISUAL viewport; Chromium reports them relative to the LAYOUT viewport
  // and holds its fixed origin at 0 no matter what `offsetTop` says. The 8px of
  // scrollbar slack that `style.css` creates exists on Chromium too, so
  // asserting the identity everywhere would turn a real Chromium offset into a
  // red lane over an assertion with no product content behind it. The two
  // product claims either side of this — `topMatchesVisualTop` and
  // `footerInsideVisualBounds` — stay engine-agnostic, because both are
  // measured through the origin and so hold in either convention.
  const expectsNegatedOrigin = browserName === 'webkit'

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
            ...(expectsNegatedOrigin
              ? { fixedOriginNegatesVisualOffsetTop: measured.fixedOriginNegatesVisualOffsetTop }
              : {}),
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
      ...(expectsNegatedOrigin ? { fixedOriginNegatesVisualOffsetTop: true } : {}),
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

test('@mobile archive controls stay inside the viewport with long localized labels', async ({ page, request }) => {
  const auth = await registerAndAttachSession(page, request, 'archive-geometry', { theme: 'legacy' })
  const boardResponse = await request.post(`${API_BASE_URL}/boards`, {
    headers: { Authorization: `Bearer ${auth.token}` },
    data: { name: `Archivio con nome molto lungo ${Date.now()}`, description: 'Archive geometry fixture' },
  })
  expect(boardResponse.ok()).toBeTruthy()
  const board = (await boardResponse.json()) as { id: string }

  const archiveResponse = await request.put(`${API_BASE_URL}/boards/${board.id}`, {
    headers: { Authorization: `Bearer ${auth.token}` },
    data: { isArchived: true },
  })
  expect(archiveResponse.ok()).toBeTruthy()

  await page.goto('/workspace/archive')
  await expect(page.getByRole('heading', { name: 'Archive', exact: true })).toBeVisible()
  await expect(page.locator('.paper-archive__row').first()).toBeVisible()

  await page.locator('.paper-archive__toggle-hidden').evaluate((element) => {
    element.textContent = 'Mostra tutte le schede archiviate e nascoste'
  })
  await page.locator('.paper-archive__refresh').evaluate((element) => {
    element.textContent = 'Aggiorna inventario degli elementi archiviati'
  })
  await page.locator('.paper-archive__input').evaluate((element) => {
    const option = document.createElement('option')
    option.value = 'localized-long-label'
    option.textContent = 'Tutti i tipi di elementi archiviati'
    element.append(option)
    element.value = option.value
  })

  const viewportSize = page.viewportSize()
  expect(viewportSize).not.toBeNull()

  await page.setViewportSize({ width: 1280, height: viewportSize!.height })
  const desktopFlow = await page.evaluate(() => {
    const rectFor = (selector: string) => {
      const rect = document.querySelector<HTMLElement>(selector)?.getBoundingClientRect()
      if (!rect) throw new Error(`Missing geometry target: ${selector}`)
      return { top: rect.top, bottom: rect.bottom }
    }

    return {
      sectionTitle: rectFor('.paper-archive__section-header .paper-archive__section-title'),
      hiddenBoardsToggle: rectFor('.paper-archive__toggle-hidden'),
      filter: rectFor('.paper-archive__input'),
      refresh: rectFor('.paper-archive__refresh'),
    }
  })
  expect(desktopFlow.hiddenBoardsToggle.top).toBeLessThan(desktopFlow.sectionTitle.bottom)
  expect(desktopFlow.hiddenBoardsToggle.bottom).toBeGreaterThan(desktopFlow.sectionTitle.top)
  expect(desktopFlow.refresh.top).toBeLessThan(desktopFlow.filter.bottom)
  expect(desktopFlow.refresh.bottom).toBeGreaterThan(desktopFlow.filter.top)

  for (const width of [375, 390]) {
    await page.setViewportSize({ width, height: viewportSize!.height })
    await expect.poll(async () => page.evaluate(() => document.documentElement.clientWidth)).toBe(width)

    const geometry = await page.evaluate(() => {
      const selectors = [
        '.paper-archive__toggle-hidden',
        '.paper-archive__input',
        '.paper-archive__refresh',
        '.paper-archive__actions > *',
      ]
      const controls = selectors.flatMap((selector) =>
        Array.from(document.querySelectorAll<HTMLElement>(selector)),
      )
      const rects = controls.map((control) => {
        const rect = control.getBoundingClientRect()
        return { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom }
      })
      const rectFor = (selector: string) => {
        const rect = document.querySelector<HTMLElement>(selector)?.getBoundingClientRect()
        if (!rect) throw new Error(`Missing geometry target: ${selector}`)
        return { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom }
      }
      const actionOrder = Array.from(document.querySelectorAll('.paper-archive__actions > *'))
        .map((control) => control.textContent?.trim())

      return {
        clientWidth: document.documentElement.clientWidth,
        scrollWidth: document.documentElement.scrollWidth,
        rects,
        sectionTitle: rectFor('.paper-archive__section-header .paper-archive__section-title'),
        hiddenBoardsToggle: rectFor('.paper-archive__toggle-hidden'),
        filter: rectFor('.paper-archive__input'),
        refresh: rectFor('.paper-archive__refresh'),
        actionOrder,
      }
    })

    expect(geometry.scrollWidth - geometry.clientWidth).toBeLessThanOrEqual(1)
    expect(geometry.rects).toHaveLength(7)
    for (const rect of geometry.rects) {
      expect(rect.left).toBeGreaterThanOrEqual(-1)
      expect(rect.right).toBeLessThanOrEqual(width + 1)
    }
    expect(geometry.hiddenBoardsToggle.top).toBeGreaterThanOrEqual(geometry.sectionTitle.bottom - 1)
    expect(geometry.refresh.top).toBeGreaterThanOrEqual(geometry.filter.bottom - 1)
    expect(geometry.actionOrder).toEqual([
      'View captures',
      'View decisions',
      'Restore Board',
      'Hide',
    ])

    const refresh = page.locator('.paper-archive__refresh')
    await refresh.focus()
    const focusGeometry = await refresh.evaluate((element) => {
      const rect = element.getBoundingClientRect()
      const style = getComputedStyle(element)
      return {
        active: document.activeElement === element,
        insideViewport: rect.left >= 0 && rect.right <= window.innerWidth,
        focusRing: style.outlineStyle !== 'none' || style.boxShadow !== 'none',
      }
    })
    expect(focusGeometry).toEqual({ active: true, insideViewport: true, focusRing: true })
  }
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
