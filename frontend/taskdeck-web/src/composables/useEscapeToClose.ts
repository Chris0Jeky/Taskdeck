import { watch } from 'vue'
import { registerEscapeHandler } from './useEscapeStack'

export function useEscapeToClose(isOpen: () => boolean, onClose: () => void) {
  watch(
    isOpen,
    (open, _, onCleanup) => {
      if (!open) {
        return
      }

      const unregisterEscapeHandler = registerEscapeHandler(onClose)
      onCleanup(() => {
        unregisterEscapeHandler()
      })
    },
    { immediate: true }
  )
}
