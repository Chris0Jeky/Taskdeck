import type { AppShellShortcutAction } from './keyboardShortcuts'

const KEYBOARD_OWNING_SURFACE_SELECTOR = [
  'dialog[open]',
  '[role="alertdialog"]',
  '[aria-modal="true"]',
].join(', ')

const TEXT_ENTRY_TARGET_SELECTOR = 'input, textarea, select, [contenteditable]:not([contenteditable="false"])'

/**
 * Whether a key event started in a form control or editable region.
 *
 * AppShell uses this before looking for active modal surfaces, so ordinary
 * typing does not pay for a document-wide surface scan.
 */
export function isTextEntryTarget(target: EventTarget | null): boolean {
  if (typeof Element === 'undefined' || !(target instanceof Element)) return false

  return target.matches(TEXT_ENTRY_TARGET_SELECTOR) || target.closest(TEXT_ENTRY_TARGET_SELECTOR) !== null
}

/**
 * Return the connected, visible surfaces that own global keyboard input.
 *
 * A bare `[role="dialog"]` is intentionally not enough: Paper's desktop card
 * inspector uses that role while leaving the board usable. Native open dialogs,
 * alert dialogs and explicit `aria-modal="true"` surfaces are the contract.
 * Hidden or inert ancestors do not own input while they are unavailable.
 */
export function activeKeyboardOwningSurfaces(): HTMLElement[] {
  if (typeof document === 'undefined' || typeof window === 'undefined') return []

  return Array.from(document.querySelectorAll<HTMLElement>(KEYBOARD_OWNING_SURFACE_SELECTOR))
    .filter((surface) => {
      if (!surface.isConnected) return false
      if (surface.closest('[hidden], [aria-hidden="true"], [inert]')) return false

      const style = window.getComputedStyle(surface)
      return style.display !== 'none' && style.visibility !== 'hidden'
    })
}

/**
 * Whether an AppShell action can be handled while the listed surfaces are open.
 *
 * With no active surface every action is available. Once a surface exists, the
 * action must name its own shell surface and every active surface must identify
 * itself as a shell surface; this keeps app-level shortcuts from crossing into
 * unrelated modal UI while still allowing a shell surface to close itself.
 */
export function shellSurfaceOwnsAction(
  surfaces: readonly HTMLElement[],
  action: AppShellShortcutAction,
): boolean {
  if (surfaces.length === 0) return true

  return surfaces.some((surface) => surface.dataset.shellSurface === action.type) &&
    surfaces.every((surface) => surface.dataset.shellSurface !== undefined)
}
