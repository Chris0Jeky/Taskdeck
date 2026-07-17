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
  /** Expected dialog type — asserted when provided (e.g. 'confirm', 'prompt'). */
  type?: Dialog['type'] extends () => infer R ? R : string
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
 * no dialog fires within `timeout`.
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
  const triggerPromise = Promise.resolve(trigger())
  // Swallow the trigger promise's rejection until we await it below, so a click
  // that fails before any dialog fires doesn't surface as an unhandled rejection
  // while we're still waiting on `dialogPromise`.
  triggerPromise.catch(() => {})

  let dialog: Dialog
  try {
    dialog = await dialogPromise
  } catch {
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
