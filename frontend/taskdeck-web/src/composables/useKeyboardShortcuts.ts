import { onMounted, onUnmounted } from 'vue'

export interface ShortcutConfig {
  key: string
  ctrl?: boolean
  shift?: boolean
  alt?: boolean
  description: string
  action: () => void
  preventDefault?: boolean
  enabled?: () => boolean
}

/**
 * Composable for handling keyboard shortcuts in components
 *
 * @param shortcuts Array of shortcut configurations
 * @returns handleKeyDown function for manual event handling if needed
 *
 * @example
 * ```ts
 * useKeyboardShortcuts([
 *   { key: 'j', description: 'Next card', action: selectNextCard },
 *   { key: 'Enter', description: 'Open card', action: openCard },
 *   { key: 'n', ctrl: true, description: 'New card', action: createCard }
 * ])
 * ```
 */
export function useKeyboardShortcuts(shortcuts: ShortcutConfig[]) {
  const handleKeyDown = (event: KeyboardEvent) => {
    // Text-entry controls own every key except Escape. Other interactive
    // controls own native activation keys, while board-navigation keys remain
    // available from card/collapse buttons for the roving-focus model.
    const target = event.target instanceof Element ? event.target : null
    const isTextEntry = Boolean(target?.closest(
      'input, textarea, select, [contenteditable]:not([contenteditable="false"])',
    ))
    const isActivationControl = Boolean(target?.closest(
      'button, a[href], [role="button"], [role="menuitem"], [role="option"], [role="tab"]',
    ))

    if (
      (isTextEntry && event.key !== 'Escape') ||
      (isActivationControl && (event.key === 'Enter' || event.key === ' '))
    ) {
      return
    }

    for (const shortcut of shortcuts) {
      const keyMatches = event.key.toLowerCase() === shortcut.key.toLowerCase()
      const ctrlMatches = shortcut.ctrl === undefined || shortcut.ctrl === (event.ctrlKey || event.metaKey)
      const shiftMatches = shortcut.shift === undefined || shortcut.shift === event.shiftKey
      const altMatches = shortcut.alt === undefined || shortcut.alt === event.altKey
      const enabled = shortcut.enabled === undefined || shortcut.enabled()

      if (enabled && keyMatches && ctrlMatches && shiftMatches && altMatches) {
        if (shortcut.preventDefault !== false) {
          event.preventDefault()
        }
        shortcut.action()
        break
      }
    }
  }

  onMounted(() => {
    window.addEventListener('keydown', handleKeyDown)
  })

  onUnmounted(() => {
    window.removeEventListener('keydown', handleKeyDown)
  })

  return { handleKeyDown }
}
