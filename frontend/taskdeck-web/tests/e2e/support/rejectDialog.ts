import type { Page } from '@playwright/test'
import { expect } from '@playwright/test'

/**
 * Accept the reject-reason dialog — the gate that collects why a proposal is
 * being turned down.
 *
 * GH-1969 replaced the native `window.prompt('Optional rejection reason:')`
 * with the app's own `TdDialog` (`RejectProposalDialog.vue`) on BOTH the Paper
 * and Legacy skins, so `expectDialog` — which listens for browser dialog
 * events — no longer sees anything and would hang out its full timeout. This
 * helper is the reject-side sibling of `expectApplyConfirmDialog` and keeps the
 * same HARD assertion the native path had: if the reason gate is ever removed,
 * the dialog never appears and the test FAILS here rather than sliding past a
 * rejection that collected nothing.
 *
 * @param page      the page under test
 * @param trigger   the action that should open the gate (the Reject button)
 * @param options.reason  the reason to submit. Defaults to a non-empty string
 *                  ON PURPOSE: the reason is OPTIONAL for Low/Medium risk but
 *                  REQUIRED for High/Critical, where the accept button stays
 *                  disabled until the box is non-blank. Filling it always keeps
 *                  the rejection succeeding regardless of how a fixture's
 *                  proposal happens to be risk-classified.
 * @param options.timeout bounded wait for the dialog. Default 15000ms.
 */
export async function expectRejectDialog(
  page: Page,
  trigger: () => Promise<unknown>,
  options: { reason?: string; timeout?: number } = {},
): Promise<void> {
  const timeout = options.timeout ?? 15_000
  const reason = options.reason ?? 'e2e: rejected by test'

  await trigger()

  const dialog = page.getByTestId('reject-dialog')
  await expect(
    dialog,
    'expected the in-app rejection-reason dialog to open — if it is absent the '
      + 'reason gate has been removed or reverted to a native prompt (GH-1969)',
  ).toBeVisible({ timeout })

  // The gate must name the proposal it is about to reject.
  await expect(page.getByTestId('reject-dialog-summary')).not.toBeEmpty()

  await page.getByTestId('reject-dialog-reason').fill(reason)
  await page.getByTestId('reject-dialog-accept').click()
  await expect(dialog).toBeHidden({ timeout })
}
