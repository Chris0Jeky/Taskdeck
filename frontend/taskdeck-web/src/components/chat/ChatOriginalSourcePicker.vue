<script setup lang="ts">
import { onScopeDispose, ref, watch } from 'vue'
import { chatSourcesApi } from '../../api/chatSourcesApi'
import { useSessionStore } from '../../store/sessionStore'
import type { ChatAssetOption, ChatAssetReference } from '../../types/chat'

const props = defineProps<{
  memoryId: string; memoryTitle?: string; boardId: string; revision: number; disabled?: boolean
  selected: ChatAssetReference[]; selectedCount: number
}>()
const emit = defineEmits<{ change: [selection: ChatAssetReference[]] }>()
const session = useSessionStore()
const items = ref<ChatAssetOption[]>([])
const loaded = ref(false)
const loading = ref(false)
const error = ref('')
const nextAfterOrdinal = ref<number | null>(-1)
let generation = 0
watch([() => props.memoryId, () => props.boardId, () => props.revision, () => session.userId, () => !!session.token], () => {
  generation++; items.value = []; loaded.value = false; loading.value = false; error.value = ''; nextAfterOrdinal.value = -1
  emit('change', [])
}, { flush: 'sync' })
onScopeDispose(() => { generation++ })

async function load() {
  if (props.disabled || loading.value || nextAfterOrdinal.value === null) return
  const request = generation
  loading.value = true; error.value = ''
  try {
    const page = await chatSourcesApi.list(props.memoryId, props.boardId, props.revision, nextAfterOrdinal.value)
    if (request !== generation) return
    if (page.memoryId.toLowerCase() !== props.memoryId.toLowerCase() || page.revision !== props.revision ||
      page.items.length > 10 || page.items.some((item, index) => !/^[a-f0-9]{64}$/i.test(item.contentHash) || item.excerpt.length > 1500
        || !Number.isSafeInteger(item.ordinal) || item.ordinal <= (index ? page.items[index - 1]!.ordinal : nextAfterOrdinal.value!)) ||
      (page.nextAfterOrdinal !== null && (page.items.length !== 10 || page.nextAfterOrdinal !== page.items.at(-1)?.ordinal))) throw new Error('Source identity changed')
    items.value = [...items.value, ...page.items.filter(item => !items.value.some(current => current.id === item.id))]
    nextAfterOrdinal.value = page.nextAfterOrdinal; loaded.value = true
  } catch (cause) {
    if (request === generation) {
      const status = (cause as { response?: { status?: number } }).response?.status
      if (status === 403 || status === 404 || status === 409) {
        items.value = []; loaded.value = false; nextAfterOrdinal.value = -1; emit('change', [])
        error.value = status === 409
          ? 'This memory changed. Refresh sources and clear selection before choosing its originals again.'
          : 'You no longer have access to these private originals. Check board access before retrying.'
      } else error.value = 'Originals could not be checked. Retry, or refresh all sources if this memory changed.'
    }
  } finally { if (request === generation) loading.value = false }
}
function toggle(asset: ChatAssetOption, checked: boolean) {
  if (props.disabled || (checked && props.selectedCount >= 5)) return
  emit('change', checked ? [...props.selected, { memoryId: props.memoryId, revision: props.revision, assetId: asset.id, contentHash: asset.contentHash }]
    : props.selected.filter(item => item.assetId !== asset.id))
}
</script>

<template>
  <div class="original-choices">
    <button v-if="!loaded || nextAfterOrdinal !== null" type="button" :disabled="disabled || loading"
      :aria-label="`${loading ? 'Checking originals' : loaded ? 'Load more originals' : 'Choose original sources'} for ${memoryTitle || 'this memory'}`" @click="load">
      {{ loading ? 'Checking originals…' : loaded ? 'Load more originals' : 'Choose original sources' }}
    </button>
    <p v-if="error" role="alert">{{ error }}</p>
    <p v-if="loaded && !items.length">No original text sources are available. Older memories can be preserved from Memory.</p>
    <p v-if="items.length">Choose exact saved text. Superseded answers remain historical evidence. Each selected original counts toward the five private sources.</p>
    <label v-for="asset in items" :key="asset.id">
      <input type="checkbox" :checked="selected.some(item => item.assetId === asset.id)"
        :disabled="disabled || (selectedCount >= 5 && !selected.some(item => item.assetId === asset.id))"
        @change="toggle(asset, ($event.target as HTMLInputElement).checked)" />
      <span>{{ asset.name }} · {{ asset.supersededByAssetId ? 'Superseded answer' : 'Saved original' }}
        <small>{{ asset.excerpt }}{{ asset.truncated ? ' [excerpt]' : '' }}</small>
        <details><summary>Source fingerprint</summary><code>{{ asset.contentHash }}</code></details>
      </span>
    </label>
  </div>
</template>

<style scoped>
.original-choices { margin: .5rem 0 1rem 1.5rem; }
button { color: inherit; background: var(--td-surface-primary); border: 1px solid var(--td-border-default); border-radius: .3rem; padding: .5rem; }
label { display: flex; gap: .5rem; align-items: start; margin-block: .75rem; }
span { min-width: 0; overflow-wrap: anywhere; }
small { display: block; white-space: pre-wrap; max-height: 8rem; overflow: auto; }
p, small, details { font-size: .85rem; line-height: 1.5; }
code { overflow-wrap: anywhere; }
</style>
