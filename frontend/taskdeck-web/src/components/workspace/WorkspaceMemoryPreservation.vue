<script setup lang="ts">
import { computed, onScopeDispose, ref, watch } from 'vue'
import { workspaceInsightsApi } from '../../api/workspaceInsights'
import { useSessionStore } from '../../store/sessionStore'
import type { Memory } from '../../types/workspaceInsights'

const props = defineProps<{ boardId: string; memories: Memory[]; disabled?: boolean }>()
const emit = defineEmits<{ preserved: [memories: Memory[]] }>()
const session = useSessionStore()
const pending = computed(() => props.memories.filter(memory => memory.boardId === props.boardId && !memory.sources))
const loading = ref(false)
const error = ref<string | null>(null)
let generation = 0
const identity = computed(() => JSON.stringify([session.userId, !!session.token, props.boardId, props.memories.map(x => [x.id, x.revision])]))
watch(identity, () => { generation++; loading.value = false; error.value = null }, { flush: 'sync' })
onScopeDispose(() => { generation++ })

async function preserve() {
  if (loading.value || props.disabled || !session.userId || !session.token || !pending.value.length) return
  const request = ++generation
  const selected = pending.value.slice(0, 50).map(({ id, revision }) => ({ id, revision }))
  const boardId = props.boardId
  loading.value = true
  error.value = null
  try {
    const saved = await workspaceInsightsApi.preserveMemorySources(boardId, selected)
    if (request !== generation) return
    if (saved.length !== selected.length || new Set(saved.map(x => x.id)).size !== selected.length
      || saved.some(x => x.boardId !== boardId || !x.sources || !selected.some(item => item.id === x.id && item.revision + 1 === x.revision)))
      throw new Error('The preserved memories changed. Reload memory before continuing.')
    emit('preserved', saved)
  } catch (failure) {
    if (request === generation) error.value = failure instanceof Error ? failure.message : 'Unable to preserve originals. Reload memory and try again.'
  } finally {
    if (request === generation) loading.value = false
  }
}
</script>

<template>
  <section v-if="pending.length" class="memory-preservation" aria-label="Preserve older memory originals">
    <p>{{ pending.length }} displayed {{ pending.length === 1 ? 'memory predates' : 'memories predate' }} preserved sources. Preserve the saved original words and correction history for source viewing and export. This keeps originals privately even after board deletion, until account deletion.</p>
    <button type="button" :disabled="disabled || loading || !session.userId || !session.token" @click="preserve">
      {{ loading ? 'Preserving originals…' : `Preserve originals for ${Math.min(pending.length, 50)} ${pending.length === 1 ? 'memory' : 'memories'}` }}
    </button>
    <p v-if="pending.length > 50">Continue in batches of 50. The current batch is saved together; a conflict saves none of it.</p>
    <p v-if="error" role="alert">{{ error }}</p>
  </section>
</template>

<style scoped>
.memory-preservation { margin-block: 1rem; padding: 1rem; border: 1px solid var(--td-border-default); border-radius: .5rem; color: var(--td-text-secondary); background: var(--td-surface-raised); }
button { cursor: pointer; padding: .5rem .75rem; border: 1px solid var(--td-border-default); border-radius: .35rem; color: var(--td-text-primary); background: var(--td-surface-raised); }
button:disabled { cursor: default; opacity: .6; }
[role=alert] { color: var(--td-color-error); }
</style>
