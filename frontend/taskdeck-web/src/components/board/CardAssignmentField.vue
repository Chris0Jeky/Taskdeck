<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { cardsApi } from '../../api/cardsApi'
import { useSessionStore } from '../../store/sessionStore'
import type { BoardParticipant, Card, CardAssignment } from '../../types/board'
import CardAssignees from './CardAssignees.vue'

const props = defineProps<{ card: Card; readOnly: boolean; disabled?: boolean }>()
const emit = defineEmits<{
  saved: [card: Card, previousVersion?: string]
  'dirty-change': [dirty: boolean]
  'saving-change': [saving: boolean]
}>()
const session = useSessionStore()
const participants = ref<BoardParticipant[]>([])
const selected = ref<string[]>([])
const baseline = ref<string[]>([])
const version = ref('')
const displayedAssignments = ref<CardAssignment[]>([])
const archived = ref(false)
const loading = ref(false)
const saving = ref(false)
const loadFailed = ref(false)
const needsRefresh = ref(false)
/*
 * How the newest settled save failed (#2982). `permission` is a 403: edit
 * permission was revoked between opening this card and the PUT, so describing it
 * as an uncertain save and offering a retry sends the user round a loop the
 * server will keep refusing. It is the one sticky class. A Viewer still reads
 * participants and the card successfully, so a completed refresh is NOT evidence
 * of write permission and must not unlock the controls; a board `canWrite`
 * cached from before the downgrade is not evidence either. Only the parent's
 * server-derived `readOnly` input turning writable again, or a different
 * card/session, clears it. Every other class stays a per-attempt outcome the
 * user can act on. Narrower than `isAccessDeniedError` (403 OR 404) on purpose:
 * a 404 here is a card/board-gone fact, not a permission signal.
 */
const saveFailure = ref<'permission' | 'conflict' | 'ineligible' | 'unknown' | null>(null)
let generation = 0
const permissionLost = computed(() => saveFailure.value === 'permission')
const error = computed(() => {
  if (permissionLost.value) return 'Your edit permission was revoked, so this assignment save was refused. Assignment editing stays locked until your board access is restored — refreshing will not unlock it. Your draft and the current assignees stay readable.'
  if (loadFailed.value) return 'Could not load current participants. Your draft is kept.'
  if (saveFailure.value === 'conflict') return 'The card changed. Refresh current assignments, review your kept draft, then save again.'
  if (saveFailure.value === 'ineligible') return 'A selected person is no longer eligible. Refresh participants and correct your kept draft.'
  if (saveFailure.value === 'unknown') return 'Could not confirm assignment save. Refresh before retrying. Your draft is kept.'
  return ''
})
const dirty = computed(() => [...selected.value].sort().join() !== [...baseline.value].sort().join())
const locked = computed(() => props.readOnly || archived.value || props.disabled || loading.value || saving.value || needsRefresh.value || permissionLost.value)
watch(dirty, value => emit('dirty-change', value))
/*
 * A submitted PUT cannot be recalled. The host editor needs the in-flight state
 * synchronously so its close, discard and navigation affordances never promise
 * to cancel a mutation the server already has (#2981). `flush: 'sync'` keeps the
 * host truthful inside the same click that starts or settles the save; the card
 * identity watcher below resets it, so a host that keeps this field mounted
 * across cards is told the new card is not saving.
 */
watch(saving, value => emit('saving-change', value), { immediate: true, flush: 'sync' })
function reset(card: Card) {
  displayedAssignments.value = card.assignments ?? []
  archived.value = !!card.isArchived
  baseline.value = (card.assignments ?? []).map(a => a.userId)
  selected.value = [...baseline.value]
  version.value = card.updatedAt
}
async function load(refresh = false) {
  const request = ++generation
  const card = props.card
  loading.value = true
  loadFailed.value = false
  // A read refresh clears what a retry can fix, never the revoked-permission lock.
  if (!permissionLost.value) saveFailure.value = null
  try {
    const [people, current] = await Promise.all([
      cardsApi.getParticipants(card.boardId),
      refresh ? cardsApi.getCard(card.boardId, card.id) : Promise.resolve(card),
    ])
    if (request !== generation) return
    participants.value = people
    if (refresh) {
      displayedAssignments.value = current.assignments ?? []
      archived.value = !!current.isArchived
      baseline.value = (current.assignments ?? []).map(a => a.userId)
      version.value = current.updatedAt
      // Keep the user's draft, including removed participants, for explicit correction.
      emit('saved', current)
    }
    needsRefresh.value = false
  } catch {
    if (request === generation) {
      loadFailed.value = true
      needsRefresh.value = true
    }
  } finally { if (request === generation) loading.value = false }
}
watch(() => `${props.card.boardId}:${props.card.id}:${session.userId}`, () => {
  generation++
  saving.value = false
  participants.value = []
  needsRefresh.value = false
  loadFailed.value = false
  saveFailure.value = null
  reset(props.card)
  if (!props.readOnly) void load()
}, { immediate: true })
/*
 * `readOnly` is the parent's server-derived write permission for this card. A
 * transition back to writable is the only in-place evidence this field gets that
 * authoritative permission was re-read and now allows writing, so it — never a
 * successful participant read — releases a revoked-permission lock.
 */
watch(() => props.readOnly, readOnly => {
  if (!readOnly) {
    saveFailure.value = null
    void load()
  }
})
watch(() => props.card.updatedAt, () => {
  if (!dirty.value && !saving.value) reset(props.card)
})
function cancel() { selected.value = [...baseline.value] }
async function save() {
  if (locked.value || !dirty.value) return
  const request = ++generation
  const card = props.card
  saving.value = true
  saveFailure.value = null
  const previousVersion = version.value
  try {
    const saved = await cardsApi.replaceAssignments(card.boardId, card.id, [...selected.value], version.value)
    if (request !== generation) return
    reset(saved)
    emit('saved', saved, previousVersion)
  } catch (failure) {
    if (request !== generation) return
    const status = (failure as { response?: { status?: number } }).response?.status
    saveFailure.value = status === 403 ? 'permission'
      : status === 409 ? 'conflict'
        : status === 400 ? 'ineligible'
          : 'unknown'
    // Read access survives a downgrade, so the refresh stays available for the
    // current assignees even when it can no longer lead back to a save.
    needsRefresh.value = true
  } finally { if (request === generation) saving.value = false }
}
const unavailable = computed(() => props.readOnly ? [] : selected.value.filter(id => !participants.value.some(p => p.userId === id)))
onBeforeUnmount(() => { generation++ })
</script>

<template>
  <section aria-label="Card assignments" class="space-y-2">
    <h3 class="text-sm font-semibold">Assignees</h3>
    <CardAssignees :assignments="displayedAssignments" />
    <p v-if="readOnly || archived" class="text-sm">Assignments are read-only.</p>
    <p v-if="loading" role="status">Loading participants…</p>
    <p v-if="error" role="alert">{{ error }}</p>
    <p v-if="saving" role="status">Saving assignments… this change was sent and cannot be discarded.</p>
    <button v-if="needsRefresh" type="button" :disabled="loading || saving" @click="load(true)">Refresh current assignments</button>
    <fieldset :disabled="locked" class="space-y-1">
      <legend class="sr-only">Choose board participants</legend>
      <label v-for="person in participants" :key="person.userId" class="flex gap-2">
        <input v-model="selected" type="checkbox" :value="person.userId" />
        {{ person.displayName }}
      </label>
      <label v-for="id in unavailable" :key="id" class="flex gap-2">
        <input v-model="selected" type="checkbox" :value="id" /> Unavailable participant — remove to continue
      </label>
      <p v-if="!loading && !selected.length" class="text-sm">Unassigned</p>
      <div v-if="!readOnly" class="flex gap-3">
        <button type="button" :disabled="!selected.length" @click="selected = []">Clear</button>
        <button type="button" :disabled="!dirty" @click="cancel">Cancel assignment changes</button>
        <!-- `locked` is on the ancestor fieldset too; naming it here keeps the write control
             itself reflect its disabled state rather than relying on ancestor propagation. -->
        <button type="button" :disabled="locked || !dirty || unavailable.length > 0" @click="save">{{ saving ? 'Saving…' : 'Save assignments' }}</button>
      </div>
    </fieldset>
  </section>
</template>
