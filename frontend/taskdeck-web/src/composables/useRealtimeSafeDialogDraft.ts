import { watch } from 'vue'

export interface RealtimeSafeDialogDraftOptions<T> {
  isOpen: () => boolean
  source: () => T
  sourceKey: (source: T) => string
  seed: (source: T) => void
  /**
   * Fields whose local draft should win over a same-entity realtime refresh.
   * While a dialog action is busy, the source snapshot is held so a failed
   * save or cancellation can resume reconciliation from the last draft state.
   */
  fields: RealtimeSafeDialogDraftField<T, any>[]
  /** Dialog actions such as save/archive may temporarily own all fields. */
  isBusy?: () => boolean
}

export interface RealtimeSafeDialogDraftField<T, V = unknown> {
  sourceValue: (source: T) => V
  draftValue: () => V
  apply: (value: V) => void
  equals?: (draftValue: V, sourceValue: V) => boolean
}

/**
 * Re-seeds a dialog from live state without clobbering an in-progress draft.
 *
 * Opening the dialog and switching to a different entity always use the latest
 * source. A same-entity reference replacement (for example a realtime board
 * refresh) updates only fields that still match their last server snapshot.
 * Each dialog supplies field-specific draft accessors so an untouched sibling
 * can follow the collaborator while a locally edited field remains intact.
 */
export function useRealtimeSafeDialogDraft<T>(options: RealtimeSafeDialogDraftOptions<T>) {
  let seededKey: string | null = null
  let sourceSnapshot: unknown[] | null = null

  const readSourceSnapshot = (source: T) =>
    options.fields.map((field) => field.sourceValue(source))

  watch(
    () => [options.source(), options.isOpen()] as const,
    ([source, isOpen], previous) => {
      const wasOpen = previous?.[1] ?? false
      if (!isOpen) {
        seededKey = null
        sourceSnapshot = null
        return
      }

      const sourceKey = options.sourceKey(source)
      if (!wasOpen || seededKey !== sourceKey || sourceSnapshot === null) {
        options.seed(source)
        seededKey = sourceKey
        sourceSnapshot = readSourceSnapshot(source)
        return
      }

      const nextSourceSnapshot = readSourceSnapshot(source)
      if (options.isBusy?.()) return

      options.fields.forEach((field, index) => {
        const previousSourceValue = sourceSnapshot?.[index] as never
        const sourceValue = nextSourceSnapshot[index] as never
        const draftValue = field.draftValue() as never
        if (field.equals
          ? field.equals(draftValue, previousSourceValue)
          : Object.is(draftValue, previousSourceValue)) {
          field.apply(sourceValue)
        }
      })
      sourceSnapshot = nextSourceSnapshot
    },
    { immediate: true },
  )
}
