import type { Dialog, Page } from '@playwright/test'
import { expect } from '@playwright/test'

/**
 * Hard-assert a native dialog (confirm/prompt/alert) that a UI action MUST raise.
 *
 * Why this exists (#1382): the fire-and-forget shape
 *
 *   page.once('dialog', (d) => d.accept())
 *   await button.click()
 *
 * silently no-ops when the dialog never fires. If a safety confirmation is
 * removed from the product, the guarded action still runs and the test stays
 * green — hiding the regression it was written to catch. `expectDialog` instead
 * FAILS the test with a clear message when the expected dialog is absent, and
 * (optionally) asserts the dialog's type and copy so an unrelated dialog cannot
 * be accepted by mistake.
 *
 * Determinism: this is not a sleep race. We arm `page.waitForEvent('dialog')`
 * BEFORE the triggering action, then run the action without awaiting it —
 * Playwright keeps the click promise pending while a dialog is open, so awaiting
 * it up front would deadlock. On the happy path `waitForEvent` resolves the
 * instant the synchronous `confirm()`/`prompt()` fires; only the failure path
 * consumes the bounded `timeout`, after which the test fails deterministically
 * rather than flaking. The triggering action is awaited AFTER the dialog is
 * handled, so its post-dialog effects have settled before the caller asserts.
 *
 * Failure attribution: if the trigger itself rejects before any dialog fires
 * (strict-mode violation, renamed button, navigation error), THAT error is
 * rethrown immediately — the "confirmation gate removed" diagnostic is reserved
 * for the case where the trigger ran but no dialog appeared within `timeout`.
 *
 * Usage:
 *
 *   await expectDialog(
 *     page,
 *     () => proposalCard.getByRole('button', { name: 'Apply to board' }).click(),
 *     { type: 'confirm', message: 'Apply this approved proposal to the board now?' },
 *   )
 */
export interface ExpectDialogOptions {
  /** Accept the dialog (default) or dismiss it once observed. */
  accept?: boolean
  /** Expected dialog type — asserted when provided. */
  type?: 'alert' | 'beforeunload' | 'confirm' | 'prompt'
  /**
   * Assert the dialog message. A string asserts equality (use for fully stable
   * copy); a RegExp asserts a match (use when the copy embeds dynamic names).
   */
  message?: string | RegExp
  /** Text to submit for a prompt() dialog before accepting. */
  promptText?: string
  /** Bounded wait for the dialog to appear before failing. Default 15000ms. */
  timeout?: number
}

/**
 * Run `trigger`, require the dialog it raises to appear, assert it, and handle
 * it. Returns the observed {@link Dialog}. Throws with a diagnostic message if
 * no dialog fires within `timeout`, or rethrows the trigger's own error if the
 * trigger fails before any dialog appears.
 */
export async function expectDialog(
  page: Page,
  trigger: () => Promise<unknown> | unknown,
  options: ExpectDialogOptions = {},
): Promise<Dialog> {
  const { accept = true, type, message, promptText, timeout = 15_000 } = options

  const dialogPromise = page.waitForEvent('dialog', { timeout })

  // Start the triggering action but do NOT await it here: the click promise
  // stays pending until the dialog is handled, so awaiting now would deadlock.
  // A trigger that throws SYNCHRONOUSLY must not orphan the armed waitForEvent
  // (its timeout rejection would otherwise surface ~15s later as an unhandled
  // rejection attributed to an unrelated later test), so settle the listener
  // before propagating.
  let triggerPromise: Promise<unknown>
  try {
    triggerPromise = Promise.resolve(trigger())
  } catch (syncError) {
    dialogPromise.catch(() => {})
    throw syncError
  }
  // Swallow the trigger promise's rejection until we await it below, so a click
  // that fails before any dialog fires doesn't surface as an unhandled rejection
  // while we're still waiting on `dialogPromise`. (Attaching .catch creates a
  // separate handled branch; the later `await triggerPromise` still rethrows.)
  triggerPromise.catch(() => {})

  // Reject-only view of the trigger: if the trigger fails before any dialog
  // fires, surface THAT error immediately instead of burning the dialog timeout
  // and misdiagnosing it as a removed confirmation gate. A trigger that
  // RESOLVES is deliberately not a race winner — on the happy path the click
  // promise stays pending while the dialog is open, and a resolved trigger
  // without a dialog still means "keep waiting for the dialog (or its timeout)".
  let triggerError: unknown
  let triggerRejected = false
  const triggerRejection: Promise<never> = triggerPromise.then(
    () => new Promise<never>(() => {}),
    (error: unknown) => {
      triggerRejected = true
      triggerError = error
      throw error
    },
  )

  let dialog: Dialog
  try {
    dialog = await Promise.race([dialogPromise, triggerRejection])
  } catch {
    if (triggerRejected) {
      // The trigger's own failure is the root cause (strict-mode violation,
      // renamed button, …). Settle the armed listener's eventual timeout
      // rejection, then rethrow the trigger error verbatim.
      dialogPromise.catch(() => {})
      throw triggerError
    }
    const wanted = [
      type ? `a '${type}'` : 'a',
      'dialog',
      message ? `matching ${message instanceof RegExp ? message.toString() : JSON.stringify(message)}` : null,
    ]
      .filter(Boolean)
      .join(' ')
    throw new Error(
      `expectDialog: expected ${wanted} to appear within ${timeout}ms after the triggering action, `
      + 'but none fired. The confirmation gate was likely removed — the guarded action may have '
      + 'proceeded without it.',
    )
  }

  // Capture type/message, then handle the dialog BEFORE asserting so a failed
  // assertion can never leave the dialog open (which would hang page teardown)
  // or the trigger promise forever pending.
  const actualType = dialog.type()
  const actualMessage = dialog.message()
  if (accept) {
    await dialog.accept(promptText)
  } else {
    await dialog.dismiss()
  }
  await triggerPromise

  if (type) {
    expect(actualType, `unexpected dialog type (message: ${JSON.stringify(actualMessage)})`).toBe(type)
  }
  if (message instanceof RegExp) {
    expect(actualMessage).toMatch(message)
  } else if (typeof message === 'string') {
    expect(actualMessage).toBe(message)
  }

  return dialog
}
