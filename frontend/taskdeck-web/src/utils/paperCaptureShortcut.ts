import { isTextEntryTarget } from './appShellKeyboard'

/**
 * Whether Paper's page-level Cmd/Ctrl+; capture shortcut may handle this event.
 *
 * AppShell deliberately leaves text-entry events alone so typing never pays for
 * a modal-surface scan. Paper Home and Inbox therefore have to enforce the same
 * text-entry boundary in their own window listeners rather than relying on the
 * shell guard to stop the event first.
 */
export function shouldHandlePaperCaptureShortcut(event: KeyboardEvent): boolean {
  if (event.isComposing || isTextEntryTarget(event.target)) return false

  return (event.metaKey || event.ctrlKey) && event.key === ';'
}
