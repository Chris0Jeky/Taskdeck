import { expect, test, type Locator, type Page } from '@playwright/test'
import { registerAndAttachSession } from './support/authSession'
import { addCard, addColumn, createBoard } from './support/boardUiHelpers'

async function installSyntheticVisualViewport(page: Page) {
  await page.addInitScript(() => {
    const events = new EventTarget()
    let heightOverride: number | null = null
    let offsetTop = 0
    let scale = 1

    Object.defineProperty(window, 'visualViewport', {
      configurable: true,
      value: {
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
      },
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

async function contractSyntheticVisualViewport(page: Page, height: number, offsetTop: number) {
  await page.evaluate(({ height: nextHeight, offsetTop: nextOffsetTop }) => {
    const setter = (window as Window & {
      __taskdeckSetVisualViewport?: (next: { height: number; offsetTop: number }) => void
    }).__taskdeckSetVisualViewport
    if (!setter) throw new Error('Synthetic visualViewport setter was not installed')
    setter({ height: nextHeight, offsetTop: nextOffsetTop })
  }, { height, offsetTop })
}

async function measureInLayoutViewportSpace(locator: Locator) {
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
    }
  })
}

test.beforeEach(async ({ page, request }) => {
  // Keep the mobile project's touch/coarse-pointer capabilities while crossing
  // CardModal's 768px layout breakpoint, matching a landscape tablet.
  await page.setViewportSize({ width: 900, height: 700 })
  await installSyntheticVisualViewport(page)
  await registerAndAttachSession(page, request, 'tablet-card-modal', { theme: 'legacy' })
})

test('@mobile CardModal follows a contracted visual viewport above the desktop breakpoint', async ({ page }) => {
  const viewport = page.viewportSize()
  expect(viewport).not.toBeNull()
  expect(viewport!.width).toBeGreaterThanOrEqual(768)

  const boardName = `Tablet Visual Viewport Board ${Date.now()}`
  const columnName = `Tablet Visual Viewport Column ${Date.now()}`
  const cardTitle = `Tablet Visual Viewport Card ${Date.now()}`

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

  const contractedTop = 120
  const contractedHeight = 420
  const contractedBottom = contractedTop + contractedHeight
  await contractSyntheticVisualViewport(page, contractedHeight, contractedTop)

  await expect.poll(async () => {
    const box = await measureInLayoutViewportSpace(editModal)
    return { top: Math.round(box.layoutTop), height: Math.round(box.height) }
  }).toEqual({ top: contractedTop, height: contractedHeight })

  const horizontalBounds = await editModal.boundingBox()
  expect(horizontalBounds).not.toBeNull()
  expect(Math.abs(
    horizontalBounds!.x * 2 + horizontalBounds!.width - viewport!.width,
  )).toBeLessThanOrEqual(2)

  await expect(scrollRegion).toHaveCSS('overflow-y', 'auto')
  const scrollMetrics = await scrollRegion.evaluate((element) => ({
    clientHeight: element.clientHeight,
    scrollHeight: element.scrollHeight,
  }))
  expect(scrollMetrics.scrollHeight).toBeGreaterThan(scrollMetrics.clientHeight)

  for (const name of ['Save Changes', 'Cancel', 'Delete Card']) {
    const action = editModal.getByRole('button', { name, exact: true })
    await action.scrollIntoViewIfNeeded()
    await expect(action).toBeVisible()
    const bounds = await measureInLayoutViewportSpace(action)
    expect(bounds.layoutTop).toBeGreaterThanOrEqual(contractedTop - 1)
    expect(bounds.layoutBottom).toBeLessThanOrEqual(contractedBottom + 1)
  }
})
