import { watch } from 'vue'

export function useEscapeToClose(isOpen: () => boolean, onClose: () => void) {
  watch(
    isOpen,
    (open, _, onCleanup) => {
      if (!open) {
        return
      }

      const handleEscape = (event: KeyboardEvent) => {
        if (event.key === 'Escape') {
          event.preventDefault()
          onClose()
        }
      }

      window.addEventListener('keydown', handleEscape)
      onCleanup(() => {
        window.removeEventListener('keydown', handleEscape)
      })
    },
    { immediate: true }
  )
}
