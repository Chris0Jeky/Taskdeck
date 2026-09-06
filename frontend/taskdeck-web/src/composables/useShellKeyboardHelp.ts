import { inject, provide, type InjectionKey } from 'vue'

/**
 * The shell's single keyboard-help surface, exposed to routed views.
 *
 * AppShell owns `showKeyboardHelp` and renders exactly one help surface for the
 * active skin (PaperShortcutsOverlay or ShellKeyboardHelp). A control deep
 * inside a routed view -- the Legacy board toolbar's help button sits four
 * levels below AppShell, behind `<router-view>` -- cannot reach that state with
 * an emit, so AppShell provides this seam instead of the view keeping a second,
 * divergent help dialog of its own (#2007).
 *
 * `open` is deliberately the whole contract: `?` owns toggling, and a button
 * that closes the surface it just opened is not what the toolbar means.
 */
export type ShellKeyboardHelpControl = Readonly<{
  open: () => void
}>

export const SHELL_KEYBOARD_HELP: InjectionKey<ShellKeyboardHelpControl> =
  Symbol('shell-keyboard-help')

export function provideShellKeyboardHelp(control: ShellKeyboardHelpControl): void {
  provide(SHELL_KEYBOARD_HELP, control)
}

/**
 * Returns `null` outside the shell rather than a silent no-op control, so a
 * caller has to decide what an absent shell means instead of a button quietly
 * doing nothing. Every route that renders a help control has
 * `meta.requiresShell`, so `null` is a wiring defect, not a supported state.
 */
export function useShellKeyboardHelp(): ShellKeyboardHelpControl | null {
  return inject(SHELL_KEYBOARD_HELP, null)
}
