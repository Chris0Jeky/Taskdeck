import { computed, ref, watch, type Ref } from 'vue'
import { thinkingApi } from '../api/thinkingApi'
import type { ThinkingDeck, ThinkingKind, ThinkingLayer } from '../types/thinking'

export function useThinkingDeck(boardId: Ref<string>, cardId: Ref<string>) {
  const layers = ref<ThinkingLayer[]>([])
  const revision = ref(0)
  const baseline = ref('[]')
  const loading = ref(false)
  const saving = ref(false)
  const error = ref('')
  const ready = ref(false)
  const canWrite = ref(false)
  const conflict = ref(false)
  let generation = 0
  const dirty = computed(() => JSON.stringify(layers.value) !== baseline.value)

  async function load() {
    const current = ++generation
    loading.value = true
    ready.value = false
    error.value = ''
    try {
      const deck = await thinkingApi.get(boardId.value, cardId.value)
      if (current !== generation) return
      layers.value = deck.layers
      revision.value = deck.revision
      canWrite.value = deck.canWrite
      baseline.value = JSON.stringify(deck.layers)
      conflict.value = false
      ready.value = true
    } catch {
      if (current === generation) error.value = 'Could not load this thinking deck. Check your connection and board access, then retry.'
    } finally {
      if (current === generation) loading.value = false
    }
  }

  async function save() {
    if (!ready.value || !canWrite.value || saving.value || conflict.value) return
    const current = generation
    // Snapshot the request: edits during the request remain dirty after it succeeds.
    const submitted: ThinkingLayer[] = JSON.parse(JSON.stringify(layers.value))
    saving.value = true
    error.value = ''
    try {
      const deck = await thinkingApi.save(boardId.value, cardId.value, revision.value, submitted)
      if (current !== generation) return
      revision.value = deck.revision
      baseline.value = JSON.stringify(deck.layers)
    } catch (cause) {
      if (current !== generation) return
      const status = (cause as { response?: { status?: number } }).response?.status
      conflict.value = status === 409
      error.value = status === 409
        ? 'Someone saved a newer version. Your draft is still here. Copy anything you want to keep before loading the saved version.'
        : 'Could not save. Your draft is still here. Check your connection and edit permission, then retry.'
    } finally { saving.value = false }
  }

  function add(kind: ThinkingKind) {
    if (layers.value.length >= 40) return
    layers.value.push({ id: crypto.randomUUID(), kind, title: '', body: '', items: [], selectedOptionId: null })
  }
  function acceptPromotion(deck: ThinkingDeck) {
    layers.value = deck.layers
    revision.value = deck.revision
    baseline.value = JSON.stringify(deck.layers)
  }
  function move(index: number, offset: number) {
    const target = index + offset
    if (target < 0 || target >= layers.value.length) return
    const [layer] = layers.value.splice(index, 1)
    if (layer) layers.value.splice(target, 0, layer)
  }
  watch([boardId, cardId], () => { layers.value = []; baseline.value = '[]'; void load() }, { immediate: true })
  return { layers, revision, loading, saving, ready, canWrite, error, conflict, dirty, load, save, add, move, acceptPromotion }
}
