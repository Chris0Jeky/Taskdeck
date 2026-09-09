import { expect, test } from '@playwright/test'
import { API_BASE_URL, registerAndAttachSession } from './support/authSession'
import { createCaptureItem } from './support/captureFlow'

test('Paper triage edit can cancel a held detail read without reopening', async ({ page, request }) => {
  test.setTimeout(60_000)
  const auth = await registerAndAttachSession(page, request, 'edit-load')
  const captureText = `loading-cancel-${Date.now()}`
  const capture = await createCaptureItem(request, auth, null, captureText)

  let detailSeen!: () => void
  const detailRequestSeen = new Promise<void>((resolve) => { detailSeen = resolve })
  let releaseDetail!: () => void
  const detailResponseHeld = new Promise<void>((resolve) => { releaseDetail = resolve })
  const detailUrl = `${API_BASE_URL}/capture/items/${encodeURIComponent(capture.id)}`

  await page.route(detailUrl, async (route) => {
    detailSeen()
    await detailResponseHeld
    try {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          ...capture,
          rawText: captureText,
          textExcerpt: captureText,
          canEditSuggestion: true,
        }),
      })
    } catch {
      // Cancel aborts the browser request. The late response is intentionally
      // allowed to lose the race without turning the test into a route error.
    }
  })

  await page.goto('/workspace/inbox')
  const row = page.locator('.paper-triage__row').filter({ hasText: captureText }).first()
  await expect(row).toBeVisible()
  await row.locator('button[data-action="edit"]').click()
  await expect(row.locator('[data-testid="capture-edit-loading"]')).toBeVisible()
  await detailRequestSeen

  const cancel = row.locator('button[data-action="edit-cancel"]')
  await expect(cancel).toHaveText(/Cancel/)
  await cancel.click()
  await expect(row.locator('[data-testid="capture-edit"]')).toHaveCount(0)
  await expect(row.locator('button[data-action="edit"]')).toBeEnabled()
  await expect(row.locator('button[data-action="edit"]')).toBeFocused()

  releaseDetail()
  await page.waitForTimeout(100)
  await expect(row.locator('[data-testid="capture-edit"]')).toHaveCount(0)
})
