type EscapeHandler = {
  id: number
  onEscape: () => void
}

const handlers: EscapeHandler[] = []
let nextHandlerId = 1
let listenerAttached = false

function handleEscapeKeydown(event: KeyboardEvent) {
  if (event.key !== 'Escape') {
    return
  }

  if (handlers.length === 0) {
    return
  }

  const topHandler = handlers[handlers.length - 1]
  if (!topHandler) {
    return
  }

  event.preventDefault()
  event.stopPropagation()
  topHandler.onEscape()
}

function attachListener() {
  if (listenerAttached || typeof window === 'undefined') {
    return
  }

  // Capture phase ensures top-surface Escape handling executes before page-level shortcuts.
  window.addEventListener('keydown', handleEscapeKeydown, true)
  listenerAttached = true
}

function detachListenerIfIdle() {
  if (!listenerAttached || handlers.length > 0 || typeof window === 'undefined') {
    return
  }

  window.removeEventListener('keydown', handleEscapeKeydown, true)
  listenerAttached = false
}

export function registerEscapeHandler(onEscape: () => void): () => void {
  const id = nextHandlerId++
  handlers.push({ id, onEscape })
  attachListener()

  return () => {
    const index = handlers.findIndex((handler) => handler.id === id)
    if (index !== -1) {
      handlers.splice(index, 1)
    }
    detachListenerIfIdle()
  }
}
