<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { workspaceInsightsApi } from '../../api/workspaceInsights'
import { useSessionStore } from '../../store/sessionStore'
import type { Memory, MemorySourceDetail } from '../../types/workspaceInsights'

const props = defineProps<{ memory: Memory }>()
const session = useSessionStore()
const detail = ref<MemorySourceDetail | null>(null)
const error = ref<string | null>(null)
const loading = ref(false)
let generation = 0
const identity = computed(() => JSON.stringify([session.userId, session.token, props.memory.id, props.memory.boardId, props.memory.revision]))
watch(identity, () => { generation++; detail.value = null; error.value = null; loading.value = false }, { flush: 'sync' })

async function load() {
  const request = ++generation
  const receipt = props.memory.sources
  if (!receipt || !session.userId || !session.token) return
  loading.value = true
  error.value = null
  detail.value = null
  try {
    const result = await workspaceInsightsApi.getMemorySources(props.memory.id)
    if (request !== generation) return
    if (result.id !== receipt.captureId || result.boardId !== props.memory.boardId
      || !result.capture.sourceAssets.some(asset => asset.id === receipt.answerAssetId))
      throw new Error('These originals no longer match the saved memory. Reload the memory and try again.')
    detail.value = result
  } catch (failure) {
    if (request === generation) error.value = failure instanceof Error ? failure.message : 'Unable to load originals. Try again.'
  } finally {
    if (request === generation) loading.value = false
  }
}
</script>

<template>
  <details v-if="memory.sources" class="memory-sources">
    <summary>Preserved originals</summary>
    <p>Your original words and corrections are retained privately. Archiving hides the memory from active context; it does not erase these originals. Account export includes them even after board deletion. Account deletion erases them.</p>
    <button type="button" :disabled="loading" @click="load">{{ loading ? 'Loading originals…' : detail ? 'Reload originals' : 'Load originals' }}</button>
    <p v-if="error" role="alert">{{ error }}</p>
    <ol v-if="detail">
      <li v-for="asset in [...detail.capture.sourceAssets].sort((a, b) => a.ordinal - b.ordinal)" :key="asset.id">
        <strong>{{ asset.id === memory.sources.evidenceAssetId ? 'Original question and evidence' : asset.supersedesAssetId ? 'Corrected answer' : 'Original answer' }}</strong>
        <span v-if="asset.supersededByAssetId"> · Superseded; original preserved</span>
        <pre>{{ asset.text }}</pre>
        <details><summary>Source receipt</summary><p>Asset {{ asset.id }}</p><p>Content digest {{ asset.contentHash }}</p></details>
      </li>
    </ol>
  </details>
</template>

<style scoped>
.memory-sources { color: var(--td-text-secondary); font-size: .85rem; min-width: 0; overflow-wrap: anywhere; }
summary,button { cursor: pointer; }
button { padding: .5rem .75rem; border: 1px solid var(--td-border-default); border-radius: .35rem; color: var(--td-text-primary); background: var(--td-surface-raised); }
li { margin-block: .75rem; }
pre { white-space: pre-wrap; overflow-wrap: anywhere; font: inherit; color: var(--td-text-primary); }
[role=alert] { color: var(--td-color-error); }
</style>
