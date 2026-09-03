import { nextTick, onUnmounted, watch, type Ref } from 'vue'

const FOCUSABLE_SELECTOR =
  'a[href], button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])'

export interface DialogFocusManagementOptions {
  isOpen: () => boolean
  dialogRef: Ref<HTMLElement | null>
  initialFocus?: (dialog: HTMLElement) => HTMLElement | null
}

/**
 * Shared modal focus lifecycle for Taskdeck dialogs.
 *
 * Captures and restores the opener, moves focus into the dialog after render,
 * and keeps forward/backward Tab movement inside the active dialog. Escape is
 * deliberately not handled here: each surface registers with the shared
 * escape stack according to its own close contract.
 */
export function useDialogFocusManagement(options: DialogFocusManagementOptions) {
  let previouslyFocusedElement: HTMLElement | null = null

  function restoreFocus() {
    if (previouslyFocusedElement?.isConnected) {
      previouslyFocusedElement.focus()
    }
    previouslyFocusedElement = null
  }

  function focusDialog() {
    const dialog = options.dialogRef.value
    if (!dialog) return
    const initialTarget = options.initialFocus?.(dialog) ?? dialog
    initialTarget.focus()
  }

  function trapFocus(event: KeyboardEvent) {
    if (event.key !== 'Tab' || !options.dialogRef.value) return

    const focusableElements = Array.from(
      options.dialogRef.value.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR),
    )

    if (focusableElements.length === 0) {
      event.preventDefault()
      return
    }

    const first = focusableElements[0]!
    const last = focusableElements[focusableElements.length - 1]!
    const activeIndex = focusableElements.indexOf(document.activeElement as HTMLElement)

    if (event.shiftKey && activeIndex <= 0) {
      event.preventDefault()
      last.focus()
    } else if (
      !event.shiftKey &&
      (activeIndex === -1 || activeIndex === focusableElements.length - 1)
    ) {
      event.preventDefault()
      first.focus()
    }
  }

  watch(
    options.isOpen,
    async (isOpen, wasOpen) => {
      if (isOpen) {
        if (!wasOpen) {
          previouslyFocusedElement =
            document.activeElement instanceof HTMLElement ? document.activeElement : null
        }
        await nextTick()
        if (options.isOpen()) focusDialog()
      } else if (wasOpen) {
        restoreFocus()
      }
    },
    { immediate: true },
  )

  // A parent commonly removes a dialog with v-if while its `isOpen` prop is
  // still true, so unmount must restore independently of the watcher.
  onUnmounted(restoreFocus)

  return { trapFocus }
}
