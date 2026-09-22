import type { InjectionKey } from 'vue'

export interface AssignmentSaveRegistry {
  /**
   * Claim one submitted assignment write. The returned idempotent release
   * function belongs to that exact operation and remains valid after the
   * initiating component unmounts.
   */
  begin(ownerId: string): () => void
  /**
   * End the visible board session. Active old-generation operations are hidden
   * synchronously; their later releases cannot affect a replacement session.
   */
  reset(): void
}

export const assignmentSaveRegistryKey: InjectionKey<AssignmentSaveRegistry> =
  Symbol('assignment-save-registry')

export function createAssignmentSaveRegistry(
  onSavingChange: (saving: boolean) => void,
): AssignmentSaveRegistry {
  let generation = 0
  let nextToken = 0
  let aggregateSaving = false
  const activeTokens = new Set<string>()

  function publish() {
    const nextSaving = activeTokens.size > 0
    if (nextSaving === aggregateSaving) return
    aggregateSaving = nextSaving
    onSavingChange(nextSaving)
  }

  function begin(ownerId: string) {
    const operationGeneration = generation
    const token = `${operationGeneration}:${ownerId}:${nextToken++}`
    activeTokens.add(token)
    publish()

    let released = false
    return () => {
      if (released) return
      released = true
      if (operationGeneration !== generation) return
      activeTokens.delete(token)
      publish()
    }
  }

  function reset() {
    generation++
    activeTokens.clear()
    publish()
  }

  return { begin, reset }
}
