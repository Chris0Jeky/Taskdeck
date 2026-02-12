import { ref, onMounted, onUnmounted } from 'vue'

export type ShortcutContext = 
  | 'global-shell'
  | 'board-canvas'
  | 'card-editor'
  | 'column-editor'
  | 'modal/drawer'
  | 'ops-console'

interface ContextShortcut {
  key: string
  ctrl?: boolean
  shift?: boolean
  alt?: boolean
  handler: () => void
  description: string
}

const contextStack = ref<ShortcutContext[]>(['global-shell'])
const contextShortcuts = new Map<ShortcutContext, ContextShortcut[]>()

export function useShortcutContext() {
  function pushContext(context: ShortcutContext) {
    contextStack.value.push(context)
  }

  function popContext(context: ShortcutContext) {
    const index = contextStack.value.lastIndexOf(context)
    if (index !== -1) {
      contextStack.value.splice(index, 1)
    }
  }

  function activeContext(): ShortcutContext {
    return contextStack.value[contextStack.value.length - 1] ?? 'global-shell'
  }

  function registerShortcuts(context: ShortcutContext, shortcuts: ContextShortcut[]) {
    contextShortcuts.set(context, shortcuts)
  }

  function unregisterShortcuts(context: ShortcutContext) {
    contextShortcuts.delete(context)
  }

  function getActiveShortcuts(): ContextShortcut[] {
    const current = activeContext()
    return contextShortcuts.get(current) ?? []
  }

  function getAllShortcuts(): Map<ShortcutContext, ContextShortcut[]> {
    return new Map(contextShortcuts)
  }

  return {
    contextStack,
    pushContext,
    popContext,
    activeContext,
    registerShortcuts,
    unregisterShortcuts,
    getActiveShortcuts,
    getAllShortcuts,
  }
}

export function useContextualShortcuts(context: ShortcutContext, shortcuts: ContextShortcut[]) {
  const { pushContext, popContext, registerShortcuts, unregisterShortcuts, activeContext } = useShortcutContext()

  function isTypingTarget(el: EventTarget | null): boolean {
    if (!el || !(el instanceof HTMLElement)) return false
    const tag = el.tagName.toLowerCase()
    return tag === 'input' || tag === 'textarea' || el.isContentEditable
  }

  function handleKeyDown(e: KeyboardEvent) {
    if (activeContext() !== context) return

    if (isTypingTarget(e.target)) {
      // Only allow Escape and explicit save shortcuts while typing
      if (e.key !== 'Escape' && !(e.ctrlKey || e.metaKey)) return
    }

    for (const shortcut of shortcuts) {
      const ctrlMatch = shortcut.ctrl ? (e.ctrlKey || e.metaKey) : !(e.ctrlKey || e.metaKey)
      const shiftMatch = shortcut.shift ? e.shiftKey : !e.shiftKey
      const altMatch = shortcut.alt ? e.altKey : !e.altKey

      if (e.key === shortcut.key && ctrlMatch && shiftMatch && altMatch) {
        e.preventDefault()
        shortcut.handler()
        return
      }
    }
  }

  onMounted(() => {
    pushContext(context)
    registerShortcuts(context, shortcuts)
    document.addEventListener('keydown', handleKeyDown)
  })

  onUnmounted(() => {
    document.removeEventListener('keydown', handleKeyDown)
    unregisterShortcuts(context)
    popContext(context)
  })
}
