<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue'
import { cardRelationsApi } from '../../api/cardRelationsApi'
import { boardsApi } from '../../api/boardsApi'
import { cardsApi } from '../../api/cardsApi'
import { getErrorDisplay } from '../../composables/useErrorMapper'
import { useSessionStore } from '../../store/sessionStore'
import type { Proposal } from '../../types/automation'
import type { BoardDetail, Card } from '../../types/board'
import type { CardRelation, CardRelationInputType, BoardCardRelations } from '../../types/cardRelations'

const props = defineProps<{ boardId: string; cardId: string; canWrite: boolean; refreshPermission?: () => Promise<unknown> }>()
const emit = defineEmits<{ busy: [value: boolean] }>()
const session = useSessionStore()
const expanded = ref(false)
const loading = ref(false)
const saving = ref(false)
const graph = ref<BoardCardRelations | null>(null)
const board = ref<BoardDetail | null>(null)
const activeCards = ref<Card[]>([])
const archivedCards = ref<Card[]>([])
const selectedCardId = ref('')
const selectedRelationType = ref<CardRelationInputType>('relates-to')
const error = ref('')
const stale = ref(false)
const receipt = ref<Proposal | null>(null)
let generation = 0

const cards = computed(() => {
  const byId = new Map<string, Card>()
  for (const card of [...activeCards.value, ...archivedCards.value]) byId.set(card.id, card)
  return [...byId.values()]
})
const cardById = computed(() => new Map(cards.value.map(card => [card.id, card])))
const currentCard = computed(() => cardById.value.get(props.cardId) ?? null)
const currentCardIsActive = computed(() => currentCard.value !== null && currentCard.value.isArchived !== true)
const boardIsActive = computed(() => board.value?.isArchived !== true)
const canPropose = computed(() => props.canWrite && graph.value?.canWrite === true && currentCardIsActive.value && boardIsActive.value)
const canEdit = computed(() => canPropose.value && !stale.value)
const shownRelations = computed(() => graph.value?.relations.filter(relation => relation.sourceCardId === props.cardId || relation.targetCardId === props.cardId) ?? [])
const availableCards = computed(() => cards.value.filter(card => card.id !== props.cardId && card.isArchived !== true))
const selectionIsValid = computed(() => availableCards.value.some(card => card.id === selectedCardId.value))
const boardAtLimit = computed(() => (graph.value?.relations.length ?? 0) >= 500)

function cardLabel(cardId: string): string {
  const card = cardById.value.get(cardId)
  if (!card) return `Card ${cardId}`
  return `${card.title}${card.isArchived === true ? ' (archived)' : ''}`
}

function columnName(cardId: string): string {
  const card = cardById.value.get(cardId)
  if (!card) return 'Card details unavailable'
  return board.value?.columns.find(column => column.id === card.columnId)?.name ?? 'Column unavailable'
}

function relationDescription(relation: CardRelation): string {
  const source = relation.sourceCardId === props.cardId ? 'This card' : cardLabel(relation.sourceCardId)
  const target = relation.targetCardId === props.cardId ? 'this card' : cardLabel(relation.targetCardId)
  switch (relation.relationType) {
    case 'blocks': return `${source} blocks ${target}`
    case 'relates-to': return `${source} relates to ${target}`
    case 'duplicates': return `${source} duplicates ${target}`
    case 'spawned-from': return `${source} was spawned from ${target}`
  }
}

function cardRoute(cardId: string) {
  return `/workspace/boards/${props.boardId}/cards/${cardId}/thinking`
}

function clearGraph() {
  graph.value = null
  board.value = null
  activeCards.value = []
  archivedCards.value = []
}

async function load() {
  if (saving.value) return
  const current = ++generation
  loading.value = true
  error.value = ''
  stale.value = false
  try {
    const [nextGraph, nextBoard, nextActiveCards, nextArchivedCards] = await Promise.all([
      cardRelationsApi.get(props.boardId),
      boardsApi.getBoard(props.boardId),
      cardsApi.getCards(props.boardId),
      cardsApi.getArchivedCards(props.boardId),
      props.refreshPermission?.(),
    ])
    if (current !== generation) return
    graph.value = nextGraph
    board.value = nextBoard
    activeCards.value = nextActiveCards
    archivedCards.value = nextArchivedCards
    if (!nextActiveCards.concat(nextArchivedCards).some(card => card.id === props.cardId)) {
      error.value = 'This card is no longer available on this board. Refresh the workspace before proposing a relation.'
    }
    if (!nextActiveCards.some(card => card.id === selectedCardId.value)) selectedCardId.value = ''
  } catch (cause) {
    if (current !== generation) return
    clearGraph()
    error.value = getErrorDisplay(cause, 'Could not load card relations. Check board access and retry.').message
  } finally {
    if (current === generation) loading.value = false
  }
}

function toggle() {
  expanded.value = !expanded.value
  if (expanded.value) void load()
}

function relationEndpointsAreActive(relation: CardRelation): boolean {
  return cardById.value.get(relation.sourceCardId)?.isArchived !== true &&
    cardById.value.get(relation.targetCardId)?.isArchived !== true &&
    cardById.value.has(relation.sourceCardId) && cardById.value.has(relation.targetCardId)
}

async function propose(action: 'add' | 'remove', relation?: CardRelation) {
  if (!graph.value || saving.value || loading.value || stale.value || !canEdit.value) return
  const input = relation
    ? {
        boardId: props.boardId,
        cardId: relation.sourceCardId,
        relatedCardId: relation.targetCardId,
        relationType: relation.relationType,
        expectedRevision: graph.value.revision,
      }
    : {
        boardId: props.boardId,
        cardId: props.cardId,
        relatedCardId: selectedCardId.value,
        relationType: selectedRelationType.value,
        expectedRevision: graph.value.revision,
      }
  if (!input.relatedCardId || (!relation && !selectionIsValid.value)) return

  saving.value = true
  emit('busy', true)
  error.value = ''
  receipt.value = null
  try {
    receipt.value = action === 'add'
      ? await cardRelationsApi.addProposal(input)
      : await cardRelationsApi.removeProposal(input)
    if (action === 'add') selectedCardId.value = ''
  } catch (cause) {
    const status = (cause as { response?: { status?: number } })?.response?.status
    if (status === 409) {
      stale.value = true
      error.value = 'Relations changed after this view was loaded. Your selection is kept; refresh relations before proposing a change.'
    } else {
      error.value = getErrorDisplay(cause, 'Could not create a review proposal. No relation was changed.').message
    }
  } finally {
    saving.value = false
    emit('busy', false)
  }
}

function canRemove(relation: CardRelation): boolean {
  return canEdit.value && relationEndpointsAreActive(relation)
}

watch([() => props.boardId, () => props.cardId, () => session.userId], () => {
  generation++
  expanded.value = false
  loading.value = false
  saving.value = false
  selectedCardId.value = ''
  receipt.value = null
  stale.value = false
  error.value = ''
  clearGraph()
  emit('busy', false)
}, { flush: 'sync' })
onUnmounted(() => { generation++ })
</script>

<template>
  <section class="relations" aria-label="Typed card relations">
    <button type="button" class="toggle" :aria-expanded="expanded" :disabled="saving" @click="toggle">
      {{ expanded ? 'Hide relations' : 'Explore relations' }}
    </button>
    <div v-if="expanded" class="body">
      <header>
        <div><p class="eyebrow">How the work connects</p><h3>Card relations</h3></div>
        <button type="button" :disabled="loading || saving" @click="load">Refresh relations</button>
      </header>
      <p class="hint">Relations describe work context. They do not change card status, ownership, assignments, or hierarchy.</p>
      <p v-if="loading" role="status">Loading card relations…</p>
      <div v-if="error" role="alert"><p>{{ error }}</p><button type="button" :disabled="loading || saving" @click="load">Refresh relations</button></div>
      <template v-if="graph">
        <p v-if="board?.isArchived" class="hint">This board is archived. Relations remain readable and cannot be changed.</p>
        <p v-else-if="currentCard?.isArchived" class="hint">This card is archived. Relations remain readable and cannot be changed.</p>
        <p v-else-if="!canPropose" class="hint">Read-only · Proposing a relation needs active endpoints and board write access.</p>
        <p v-if="receipt" class="receipt" role="status">
          Proposal created. <RouterLink :to="`/workspace/review#proposal-${receipt.id}`">Review this relation proposal</RouterLink> before it can change the board.
        </p>
        <h4>Connected cards</h4>
        <p v-if="!shownRelations.length" class="hint">No typed relations are recorded for this card.</p>
        <ul v-else>
          <li v-for="relation in shownRelations" :key="`${relation.sourceCardId}:${relation.relationType}:${relation.targetCardId}`">
            <div>
              <strong>{{ relationDescription(relation) }}</strong>
              <small>{{ columnName(relation.sourceCardId) }} → {{ columnName(relation.targetCardId) }}</small>
              <RouterLink :to="cardRoute(relation.sourceCardId)">{{ cardLabel(relation.sourceCardId) }}</RouterLink>
              <span aria-hidden="true"> → </span>
              <RouterLink :to="cardRoute(relation.targetCardId)">{{ cardLabel(relation.targetCardId) }}</RouterLink>
            </div>
            <button v-if="canRemove(relation)" type="button" :disabled="saving" :aria-label="`Propose removing ${relationDescription(relation)}`" @click="propose('remove', relation)">Propose removal</button>
            <small v-else-if="cardById.get(relation.sourceCardId)?.isArchived || cardById.get(relation.targetCardId)?.isArchived">Archived endpoint · retained for context</small>
          </li>
        </ul>
        <form v-if="canPropose" @submit.prevent="propose('add')">
          <label>Relation type
            <select v-model="selectedRelationType" aria-label="Relation type" :disabled="saving || boardAtLimit || stale">
              <option value="relates-to">Relates to</option>
              <option value="blocks">This card blocks</option>
              <option value="depends-on">This card depends on</option>
              <option value="duplicates">This card duplicates</option>
              <option value="spawned-from">This card was spawned from</option>
            </select>
          </label>
          <label>Other card
            <select v-model="selectedCardId" aria-label="Other card" :disabled="saving || boardAtLimit || stale">
              <option value="">Choose an active card on this board</option>
              <option v-for="card in availableCards" :key="card.id" :value="card.id">{{ card.title }}</option>
            </select>
          </label>
          <button type="submit" :disabled="!selectionIsValid || saving || boardAtLimit || stale">{{ saving ? 'Creating proposal…' : 'Propose relation' }}</button>
        </form>
        <p v-if="boardAtLimit" class="hint">This board has reached its 500 relation limit.</p>
        <p v-if="stale" class="hint">Refresh relations to use the current version. No relation has been applied.</p>
      </template>
    </div>
  </section>
</template>

<style scoped>
.relations{margin-top:1.5rem;border-top:1px solid var(--td-border-default,#d7d1c6);padding-top:1rem;color:var(--td-text-primary,#332f29)}
.body{padding:1rem 0}header{display:flex;align-items:center;justify-content:space-between;gap:1rem}h3,h4{margin:.4rem 0}.eyebrow{font-size:.7rem;letter-spacing:.1em;text-transform:uppercase;margin:0}.hint,small{color:var(--td-text-secondary,#686156);font-size:.85rem;line-height:1.5}.receipt{padding:.65rem;border-radius:.45rem;background:var(--td-surface-container,#fffdf7)}ul{list-style:none;padding:0}li{display:flex;justify-content:space-between;gap:.7rem;align-items:center;padding:.65rem;border:1px solid var(--td-border-default,#d7d1c6);border-radius:.5rem;margin:.5rem 0}li div{min-width:0}li strong,li small{display:block}a{color:inherit;overflow-wrap:anywhere}button,select{font:inherit;color:inherit;background:var(--td-surface-container,#fffdf7);border:1px solid var(--td-border-default,#d7d1c6);border-radius:.4rem;padding:.5rem .65rem}button{cursor:pointer}button:disabled{opacity:.5;cursor:default}form{display:grid;grid-template-columns:repeat(2,minmax(0,1fr)) auto;align-items:end;gap:.6rem;margin:1rem 0}label{min-width:0;font-size:.85rem}select{display:block;width:100%;margin-top:.4rem}button:focus-visible,select:focus-visible,a:focus-visible{outline:2px solid var(--td-accent-primary,#677249);outline-offset:3px}button.toggle{font-weight:600}@media(max-width:520px){header,li{align-items:flex-start;flex-direction:column}form{grid-template-columns:1fr}form button{width:100%}}
</style>
