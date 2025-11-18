import { onMounted, onUnmounted } from 'vue'

export interface ShortcutConfig {
  key: string
  ctrl?: boolean
  shift?: boolean
  alt?: boolean
  description: string
  action: () => void
  preventDefault?: boolean
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
    // Ignore if typing in input/textarea (except Escape key)
    const target = event.target as HTMLElement
    const isTyping = target.tagName === 'INPUT' || target.tagName === 'TEXTAREA'

    if (isTyping && event.key !== 'Escape') {
      return
    }

    for (const shortcut of shortcuts) {
      const keyMatches = event.key.toLowerCase() === shortcut.key.toLowerCase()
      const ctrlMatches = shortcut.ctrl === undefined || shortcut.ctrl === (event.ctrlKey || event.metaKey)
      const shiftMatches = shortcut.shift === undefined || shortcut.shift === event.shiftKey
      const altMatches = shortcut.alt === undefined || shortcut.alt === event.altKey

      if (keyMatches && ctrlMatches && shiftMatches && altMatches) {
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
