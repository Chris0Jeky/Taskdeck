import { expect } from 'vitest'
import type { DOMWrapper, VueWrapper } from '@vue/test-utils'

/**
 * AC3 of GH-1949: a primary action whose preconditions are unmet must be
 * `disabled` or must render validation — never enabled-and-silent.
 *
 * WHY THIS IS A HELPER AND NOT A SOURCE SCAN. The other three halves of the
 * dead-affordance guard (`src/tests/guards/deadAnchors.spec.ts`) are repo-wide
 * scans over SFC source, because "has a click binding" and "is focusable" are
 * decidable from the template text. AC3 is not: whether a precondition is
 * *unmet* depends on component state, and whether the user is told depends on
 * what renders after the click. A repo-wide static rule for it would need
 * unsound heuristics or a new source marker on every primary action. So AC3 is
 * mechanized as a mounted-component assertion that a registry spec applies to
 * an explicit, opt-in list of primary actions instead.
 *
 * The contract, in order:
 *  1. If the trigger is `disabled`, the control is honest — nothing else is
 *     required, and the click is never dispatched.
 *  2. Otherwise the control claims to be live, so activating it must produce
 *     visible or observable feedback: a rendered validation node, a toast, or
 *     an emitted error event. Silence is the defect (#1944).
 *
 * What this does NOT prove: that the validation *text* is correct or helpful,
 * that the disabled reason is discoverable by a screen reader, or anything at
 * all about a control the registry does not list. It also cannot see feedback
 * rendered outside the mounted wrapper (a teleported global toast) unless the
 * caller passes `toastSpy`.
 */
export interface GuardedPrimaryActionOptions {
  /**
   * Human-readable description of the unmet precondition the caller has set up.
   * Reported in the failure message so a red run names the scenario, not just
   * the selector.
   */
  unmetPreconditions: string

  /**
   * Selectors that, if present after activation, count as rendered validation.
   * Defaults cover the repo's current feedback conventions.
   */
  validationSelectors?: string[]

  /**
   * A spy standing in for the toast/notification channel. Any recorded call
   * counts as feedback. Pass this when the feedback leaves the wrapper.
   */
  toastSpy?: { mock: { calls: unknown[][] } }

  /**
   * Emitted event names that count as feedback (an error surfaced to a parent).
   */
  errorEvents?: string[]
}

const DEFAULT_VALIDATION_SELECTORS = [
  '[role="alert"]',
  '[data-validation]',
  '[data-error]',
  '.paper-field__error',
  '.tk-error',
]

const DEFAULT_ERROR_EVENTS = ['error', 'invalid', 'blocked']

/**
 * The trigger as this helper needs it. `wrapper.get()` returns
 * `Omit<DOMWrapper, 'exists'>` while `wrapper.find()` returns the full
 * `DOMWrapper`; accepting the narrower shape lets callers pass either.
 */
export type PrimaryActionTrigger = Omit<DOMWrapper<Element>, 'exists'>

/** True when the wrapper's element carries a real HTML disabled state. */
function isDisabled(trigger: PrimaryActionTrigger): boolean {
  const element = trigger.element as HTMLButtonElement
  if (element.hasAttribute('disabled')) return true
  // A custom control cannot use the native attribute, so honour the ARIA form.
  return element.getAttribute('aria-disabled') === 'true'
}

/**
 * Assert the AC3 contract for one primary action.
 *
 * The caller mounts the component with the precondition already unmet and
 * passes the trigger; this helper decides nothing about how that state is set up.
 */
export async function expectGuardedPrimaryAction(
  wrapper: VueWrapper<any>,
  trigger: PrimaryActionTrigger,
  options: GuardedPrimaryActionOptions,
): Promise<void> {
  const scenario = options.unmetPreconditions

  // `get()` already throws for a missing node; a `find()` caller passes a
  // wrapper it has asserted itself. Either way there is an element by here.
  expect(trigger.element, `guarded primary action not found (${scenario})`).toBeTruthy()

  if (isDisabled(trigger)) {
    // Branch 1: the control tells the truth by being off. Done.
    return
  }

  // Branch 2: the control is enabled, so it must not be silent.
  const emittedBefore = { ...wrapper.emitted() }
  const toastCallsBefore = options.toastSpy?.mock.calls.length ?? 0

  await trigger.trigger('click')
  await wrapper.vm.$nextTick()
  await Promise.resolve()
  await wrapper.vm.$nextTick()

  const validationSelectors = options.validationSelectors ?? DEFAULT_VALIDATION_SELECTORS
  const renderedValidation = validationSelectors.some(
    (selector) => wrapper.findAll(selector).length > 0,
  )

  const toastCallsAfter = options.toastSpy?.mock.calls.length ?? 0
  const toasted = toastCallsAfter > toastCallsBefore

  const errorEvents = options.errorEvents ?? DEFAULT_ERROR_EVENTS
  const emittedAfter = wrapper.emitted()
  const emittedError = errorEvents.some((name) => {
    const after = emittedAfter[name]?.length ?? 0
    const before = emittedBefore[name]?.length ?? 0
    return after > before
  })

  expect(
    renderedValidation || toasted || emittedError,
    `Enabled-and-silent primary action (GH-1949 AC3). Scenario: ${scenario}. ` +
      'The trigger was not disabled, and activating it produced no rendered ' +
      'validation node, no toast, and no error event. Either disable the ' +
      'control while the precondition is unmet, or tell the user why nothing ' +
      'happened.',
  ).toBe(true)
}
