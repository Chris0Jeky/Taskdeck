import { watch } from 'vue'

export interface RealtimeSafeDialogDraftOptions<T> {
  isOpen: () => boolean
  source: () => T
  sourceKey: (source: T) => string
  seed: (source: T) => void
  isDirty: () => boolean
}

/**
 * Re-seeds a dialog from live state without clobbering an in-progress draft.
 *
 * Opening the dialog and switching to a different entity always use the latest
 * source. A same-entity reference replacement (for example a realtime board
 * refresh) is accepted only while the current draft still matches its last
 * seed snapshot. Each dialog owns that field-specific snapshot comparison.
 */
export function useRealtimeSafeDialogDraft<T>(options: RealtimeSafeDialogDraftOptions<T>) {
  let seededKey: string | null = null

  watch(
    () => [options.source(), options.isOpen()] as const,
    ([source, isOpen], previous) => {
      const wasOpen = previous?.[1] ?? false
      if (!isOpen) {
        seededKey = null
        return
      }

      const sourceKey = options.sourceKey(source)
      if (!wasOpen || seededKey !== sourceKey || !options.isDirty()) {
        options.seed(source)
        seededKey = sourceKey
      }
    },
    { immediate: true },
  )
}
