import { expect, test } from '@playwright/test'

for (const afterDismissal of ['no new focus', 'unrelated control', 'card switch'] as const) {
  test(`archive failure after Escape preserves focus ownership: ${afterDismissal}`, async ({ page }, testInfo) => {
    await page.route('**/__archive-focus-harness', route => route.fulfill({
      contentType: 'text/html',
      body: '<!doctype html><html><body><div id="archive-focus"></div><script type="module" src="/tests/e2e/support/cardArchiveFocusHarness.ts"></script></body></html>',
    }))
    await page.route('**/boards/focus-board/cards/*/detach-preview', route => route.fulfill({
      json: {
        cardId: 'focus-card-a', expectedUpdatedAt: '2026-09-12T10:00:00Z',
        expectedChildrenFingerprint: 'focus-fixture-v1', children: [],
      },
    }))
    let releaseFailure!: () => void
    const pendingWrite = new Promise<void>(resolve => { releaseFailure = resolve })
    let writes = 0
    await page.route('**/boards/focus-board/cards/focus-card-a/archive', async route => {
      expect(route.request().method()).toBe('POST')
      writes++
      await pendingWrite
      await route.fulfill({ status: 409, json: { errorCode: 'Conflict', message: 'Archive state changed' } })
    })
    await page.goto('/__archive-focus-harness')
    const opener = page.getByRole('button', { name: 'Archive card', exact: true })
    await opener.focus()
    await expect(opener).toBeFocused()
    await opener.press('Enter')
    const confirmation = page.getByRole('dialog', { name: 'Archive card?' })
    await confirmation.getByRole('button', { name: 'Confirm archive', exact: true }).click()
    await expect.poll(() => writes).toBe(1)
    await expect(page.getByRole('button', { name: 'Saving' })).toBeDisabled()
    await page.keyboard.press('Escape')
    await expect(confirmation).toHaveCount(0)

    // Do not insert a focus helper here: the disabled opener cannot receive the
    // dialog's restore, and native Chromium leaves focus on the document body.
    expect(await page.evaluate(() => document.activeElement === document.body)).toBe(true)
    if (afterDismissal === 'unrelated control') {
      await page.getByRole('button', { name: 'Unrelated action', exact: true }).focus()
    } else if (afterDismissal === 'card switch') {
      await page.getByRole('button', { name: 'Switch card', exact: true }).click()
    }
    releaseFailure()
    await expect(page.getByRole('button', { name: 'Saving' })).toHaveCount(0)
    await testInfo.attach('active-element-after-write', {
      body: await page.evaluate(() => document.activeElement?.outerHTML ?? 'null'),
      contentType: 'text/plain',
    })
    await expect(confirmation).toHaveCount(0)
    if (afterDismissal === 'card switch') {
      // The notice proves the old rejection has actually settled before the
      // negative assertions about the new card's local error and focus.
      await expect(page.getByTestId('write-notice')).toContainText('The archive requested for a previously viewed card could not be confirmed.')
      await expect(page.getByRole('alert')).toHaveCount(0)
      await expect(page.getByRole('button', { name: 'Switch card', exact: true })).toBeFocused()
      await expect(opener).toBeEnabled()
    } else {
      await expect(page.getByRole('alert')).toContainText('Archive state changed')
      const expectedFocus = afterDismissal === 'no new focus'
        ? page.getByRole('button', { name: 'Refresh card state', exact: true })
        : page.getByRole('button', { name: 'Unrelated action', exact: true })
      await expect(expectedFocus).toBeFocused()
    }
    expect(writes).toBe(1)
  })
}
