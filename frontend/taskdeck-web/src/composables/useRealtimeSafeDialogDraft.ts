import { watch } from 'vue'

export interface RealtimeSafeDialogDraftOptions<T> {
  isOpen: () => boolean
  source: () => T
  sourceKey: (source: T) => string
  seed: (source: T) => void
  /**
   * Fields whose local draft should win over a same-entity realtime refresh.
   * The source snapshot is advanced after every refresh, including while a
   * dialog is busy, so a later refresh is compared with the latest server
   * value rather than with an older snapshot.
   */
  fields: RealtimeSafeDialogDraftField<T>[]
  /** Dialog actions such as save/archive may temporarily own all fields. */
  isBusy?: () => boolean
}

export interface RealtimeSafeDialogDraftField<T, V = unknown> {
  sourceValue: (source: T) => V
  draftValue: () => V
  apply: (value: V) => void
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
      if (!options.isBusy?.()) {
        options.fields.forEach((field, index) => {
          if (Object.is(field.draftValue(), sourceSnapshot?.[index])) {
            field.apply(nextSourceSnapshot[index])
          }
        })
      }
      sourceSnapshot = nextSourceSnapshot
    },
    { immediate: true },
  )
}
