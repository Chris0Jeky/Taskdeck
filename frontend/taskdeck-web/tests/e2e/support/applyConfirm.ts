import type { Page } from '@playwright/test'
import { expect } from '@playwright/test'

/**
 * Accept the apply-to-board confirmation dialog — the second, explicit half of
 * the ADR-0003 two-phase apply.
 *
 * #1818 replaced the native `confirm('Apply this approved proposal to the board
 * now?')` with the app's own `TdDialog`, so `expectDialog` (which listens for
 * browser dialog events) no longer sees it. This helper keeps the same HARD
 * assertion `expectDialog` provided (#1382): if the confirmation gate is ever
 * removed, the dialog never appears and the test FAILS here, instead of the
 * guarded execute running silently behind a green test.
 *
 * @param page      the page under test
 * @param trigger   the action that should raise the confirmation. OPTIONAL:
 *                  on the Paper surface a successful approve now opens this
 *                  dialog by itself (GH-1942 collapsed the redundant middle
 *                  click), so those callers pass no trigger and the assertion
 *                  below is what proves the dialog actually appeared. The
 *                  Legacy surface still raises it from its own Apply button.
 * @param options.timeout bounded wait for the dialog. Default 15000ms.
 */
export async function expectApplyConfirmDialog(
  page: Page,
  trigger?: () => Promise<unknown>,
  options: { timeout?: number } = {},
): Promise<void> {
  const timeout = options.timeout ?? 15_000
  if (trigger) await trigger()

  const dialog = page.getByTestId('apply-confirm-dialog')
  await expect(
    dialog,
    'expected the apply-to-board confirmation dialog to open — if it is absent the '
      + 'phase-2 confirmation gate has been removed (#1818/#1382)',
  ).toBeVisible({ timeout })

  // The confirmation must name what is about to be written to the board.
  await expect(page.getByTestId('apply-confirm-summary')).not.toBeEmpty()

  await page.getByTestId('apply-confirm-accept').click()
  await expect(dialog).toBeHidden({ timeout })
}
