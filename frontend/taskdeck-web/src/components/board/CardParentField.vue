<script setup lang="ts">
import { computed, onScopeDispose, ref, watch } from 'vue'
import { cardsApi } from '../../api/cardsApi'
import type { Card } from '../../types/board'
/**
 * `canWrite` is the host's server-authoritative board write permission for the caller
 * (#3028). This field used to derive it from the loaded board payload itself, which read an
 * omitted optional `canWrite` as "no" and left an authorized writer holding a legacy payload
 * with a disabled selector. The host resolves that question once for the whole card editor
 * (`useCardTypePermission`) and every gate in it now answers from the same read; it is
 * required rather than optional so no host can silently fall back to the inference that was
 * the defect.
 */
const props = defineProps<{ card: Card; canWrite: boolean; readsBlocked?: boolean; disabled?: boolean }>()
const model = defineModel<string | null>({ required: true })
const cards = ref<Card[]>([])
const loading = ref(false)
/**
 * How the last parent-candidates read for the CURRENT request failed (#2974).
 * `permission` is a 403: board access was revoked between opening this card and
 * the read, so the choices are not coming back and the server will reject a
 * parent write too — telling the user to close and reopen would send them
 * round a loop that cannot succeed. `transient` is every other failure
 * (5xx/network/timeout), where reopening the card genuinely does retry.
 * Deliberately narrower than `isAccessDeniedError` (403 OR 404): a 404 from
 * this list endpoint is a routing/board-gone fact rather than a permission
 * signal, so it keeps the generic copy.
 */
const loadError = ref<'permission' | 'transient' | null>(null)
let generation = 0
watch([() => props.card.boardId, () => props.card.id, () => props.readsBlocked], async () => {
  const request = ++generation
  cards.value = []
  loading.value = true
  loadError.value = null
  if (props.readsBlocked) {
    loading.value = false
    return
  }
  try {
    const result = await cardsApi.getCards(props.card.boardId)
    // Every write below is gated on this request still being the newest one, so
    // an obsolete read's completion — success OR failure — never overwrites the
    // state a newer read has already established.
    if (request === generation) cards.value = result
  } catch (cause) {
    if (request === generation) {
      const status = (cause as { response?: { status?: number } })?.response?.status
      loadError.value = status === 403 ? 'permission' : 'transient'
    }
  }
  finally { if (request === generation) loading.value = false }
}, { immediate: true, flush: 'sync' })
onScopeDispose(() => { generation++ })
const candidates = computed(() => cards.value.filter(card => card.id !== props.card.id))
</script>
<template>
  <div class="my-3 space-y-1">
    <label for="card-parent" class="block text-sm font-medium">Parent card</label>
    <select id="card-parent" v-model="model" class="w-full rounded border border-outline-variant/40 bg-surface px-3 py-2"
      :disabled="disabled || readsBlocked || !canWrite || loading || loadError !== null">
      <option :value="null">No parent</option>
      <option v-if="model && !candidates.some(card => card.id === model)" :value="model">Current parent ({{ model }})</option>
      <option v-for="candidate in candidates" :key="candidate.id" :value="candidate.id">{{ candidate.title }} ({{ candidate.workItemType ?? 'Task' }})</option>
    </select>
    <p v-if="loading" role="status" class="text-sm">Loading parent choices...</p>
    <p v-else-if="loadError === 'permission'" role="alert" class="text-sm">You no longer have access to this board, so parent choices are unavailable. Reopening this card will not restore them. Ask a board admin to restore your access.</p>
    <p v-else-if="loadError === 'transient'" role="alert" class="text-sm">Parent choices could not be loaded. Close and reopen this card to retry.</p>
    <p v-else class="text-sm text-on-surface-variant">Optional, on this board. Up to four levels; all card types can be parents. Save changes to apply.</p>
  </div>
</template>
