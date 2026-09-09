<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useWorkspacePlanStore } from '../../store/workspacePlanStore'
import { usePlanCardPicker } from '../../composables/usePlanCardPicker'
import type { PlanEntry, PlanCard } from '../../api/workspacePlanApi'

const router = useRouter()
const store = useWorkspacePlanStore()
const { boards, cards, boardId, cardId, loadingBoards, loadingCards, error: pickerError, loadBoards, loadCards } = usePlanCardPicker()
function localDate(date = new Date()) {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}
const plannedDate = ref(localDate())
const view = ref<'list' | 'board' | 'horizon'>('list')
const day = ref('')
const message = ref('')
const disabled = computed(() => !store.ready || store.loading || store.saving)
const entries = computed(() => (store.plan?.entries ?? []).filter(entry => !day.value || entry.plannedDate === day.value))
const groups = computed(() => {
  const result = new Map<string, PlanEntry[]>()
  for (const entry of entries.value) {
    const key = view.value === 'horizon' ? entry.plannedDate : view.value === 'board'
      ? entry.available ? `${entry.boardName} · ${entry.columnName}` : 'Unavailable cards' : 'Your chosen work'
    result.set(key, [...(result.get(key) ?? []), entry])
  }
  return [...result.entries()].sort((a, b) => view.value === 'horizon' ? a[0].localeCompare(b[0]) : 0)
})
const duplicate = computed(() => store.plan?.entries.some(entry => entry.cardId === cardId.value))

async function add() {
  if (disabled.value || !boardId.value || !cardId.value || !plannedDate.value || duplicate.value) return
  message.value = ''
  if (await store.save([...(store.plan?.entries ?? []), { boardId: boardId.value, cardId: cardId.value, plannedDate: plannedDate.value }])) {
    cardId.value = ''
    message.value = 'Added to your plan. The card’s due date stays the same.'
  }
}
async function makeRoom(entry: PlanEntry) {
  message.value = ''
  if (await store.save((store.plan?.entries ?? []).filter(item => item.cardId !== entry.cardId)))
    message.value = 'Removed from your plan. The card and its due date stay the same.'
}
async function tomorrow(entry: PlanEntry) {
  const date = new Date()
  date.setDate(date.getDate() + 1)
  message.value = ''
  if (await store.save((store.plan?.entries ?? []).map(item => item.cardId === entry.cardId ? { ...item, plannedDate: localDate(date) } : item)))
    message.value = 'Planned for tomorrow. The card’s due date stays the same.'
}
async function focus(card: PlanCard) {
  if (!card.available) return
  message.value = ''
  if (await store.focus(card.boardId, card.cardId))
    await router.push({ path: `/workspace/boards/${card.boardId}/cards/${card.cardId}/thinking`, query: { focus: '1' } })
}
function dueDate(value: string) { return new Date(value).toLocaleDateString() }
async function retryChoices() { await loadBoards(); if (!pickerError.value && boardId.value) await loadCards() }
onMounted(() => { void store.load(); void loadBoards() })
</script>

<template>
  <div class="personal-plan">
    <header><p class="personal-plan__eyebrow">A LITTLE ROOM FOR TODAY</p><h1>Your personal plan</h1><p>Choose work you want to spend time on. This private plan keeps your choices separate from the board’s due dates.</p></header>
    <div v-if="store.error" role="alert"><p>{{ store.error }}</p><button type="button" :disabled="store.loading || store.saving" @click="store.load">Refresh personal plan</button></div>
    <p v-if="store.loading" role="status">Loading your plan…</p>
    <section v-if="store.plan?.lastWorked" class="personal-plan__resume" aria-label="Last worked on">
      <template v-if="store.plan.lastWorked.available"><div><small>LAST WORKED ON · {{ store.plan.lastWorked.boardName }}</small><h2>{{ store.plan.lastWorked.title }}</h2><p>{{ new Date(store.plan.lastWorked.workedAt).toLocaleString() }}</p></div><button type="button" :disabled="disabled" @click="focus(store.plan.lastWorked)">Resume focus</button></template>
      <p v-else>Your previous focus is no longer available. Choose another card below.</p>
    </section>
    <form class="personal-plan__picker" @submit.prevent="add">
      <h2>Choose a thread</h2>
      <p v-if="pickerError" role="alert">{{ pickerError }} <button type="button" :disabled="loadingBoards || loadingCards" @click="retryChoices">Retry card choices</button></p>
      <label>Project<select aria-label="Project" v-model="boardId" :disabled="disabled || loadingBoards"><option value="">{{ loadingBoards ? 'Loading projects…' : 'Choose a project' }}</option><option v-for="board in boards" :key="board.id" :value="board.id">{{ board.name }}</option></select></label>
      <label>Card<select aria-label="Card" v-model="cardId" :disabled="disabled || !boardId || loadingCards"><option value="">{{ loadingCards ? 'Loading cards…' : 'Choose a card' }}</option><option v-for="card in cards" :key="card.id" :value="card.id">{{ card.title }}</option></select></label>
      <label>Plan for<input v-model="plannedDate" type="date" required :disabled="disabled" /></label>
      <button type="submit" :disabled="disabled || loadingCards || loadingBoards || !boardId || !cardId || duplicate || !plannedDate || (store.plan?.entries.length ?? 0) >= 40">Add to plan</button>
      <p v-if="duplicate">This card is already in your plan.</p>
      <p v-if="!loadingBoards && !pickerError && !boards.length">Create a project and a card to begin. <RouterLink to="/workspace/boards">Open projects</RouterLink>.</p>
      <p v-else-if="boardId && !loadingCards && !cards.length && !pickerError">This project has no cards to choose yet.</p>
    </form>
    <p v-if="message" role="status">{{ message }}</p>
    <div class="personal-plan__toolbar">
      <div role="group" aria-label="Plan representation"><button v-for="option in (['list', 'board', 'horizon'] as const)" :key="option" type="button" :aria-pressed="view === option" @click="view = option">{{ option[0]!.toUpperCase() + option.slice(1) }}</button></div>
      <label>Show planned date<input v-model="day" type="date" /></label><button type="button" @click="day = localDate()">Today</button><button type="button" @click="day = ''">All dates</button>
      <button type="button" :disabled="store.loading || store.saving" @click="store.load">Refresh card status</button>
    </div>
    <p v-if="view === 'horizon'">Horizon groups work by your chosen plan date. Card deadlines are shown separately.</p>
    <p v-else-if="view === 'board'">Board groups your chosen work by its current project and column. Open the board to move a card.</p>
    <p v-if="store.ready && !entries.length">{{ day ? 'Nothing chosen for this date. Your other planned work is still under All dates.' : 'Your plan has room. Choose a card above, then focus on one thread at a time.' }}</p>
    <div class="personal-plan__groups" :data-view="view">
      <section v-for="[name, items] in groups" :key="name" class="personal-plan__group" :aria-label="name">
        <h2>{{ name }}</h2>
        <article v-for="entry in items" :key="entry.cardId" class="personal-plan__card">
          <h3>{{ entry.available ? entry.title : 'Card unavailable' }}</h3>
          <p v-if="entry.available">{{ entry.boardName }} · {{ entry.columnName }}</p>
          <p>Planned for {{ entry.plannedDate }}<span v-if="entry.dueDate"> · Card due {{ dueDate(entry.dueDate) }}</span></p>
          <p v-if="entry.isBlocked" class="personal-plan__blocked">Blocked: {{ entry.blockReason || 'No reason recorded' }}</p>
          <p v-if="!entry.available">It may be archived, removed, or no longer shared with you. You can make room in your plan.</p>
          <div class="personal-plan__actions"><button type="button" :disabled="disabled || !entry.available" @click="focus(entry)">Focus</button><button type="button" :disabled="disabled || !entry.available" @click="tomorrow(entry)">Plan tomorrow</button><button type="button" :disabled="disabled" @click="makeRoom(entry)">Make room</button><RouterLink v-if="entry.available" :to="`/workspace/boards/${entry.boardId}`">Open board</RouterLink></div>
        </article>
      </section>
    </div>
  </div>
</template>

<style scoped>
.personal-plan{max-width:1250px;margin:auto;display:grid;gap:24px;color:var(--ink,var(--td-text-primary));font-family:var(--sans,system-ui,sans-serif)}h1{font:450 clamp(30px,4vw,48px)/1.15 var(--serif,Georgia,serif);margin:0 0 16px}h2{font-size:20px;margin:0 0 12px}h3{font-size:17px;margin:0;overflow-wrap:anywhere}.personal-plan p{font-size:14px;line-height:1.6;margin:8px 0;color:var(--ink-muted,var(--td-text-secondary))}.personal-plan__eyebrow{font-size:11px!important;letter-spacing:.14em}.personal-plan button,.personal-plan select,.personal-plan input{color:inherit;background:var(--paper-card,var(--td-surface-raised));border:1px solid var(--line,var(--td-border-default));border-radius:8px;min-height:44px;padding:10px 14px;font:inherit;font-size:13px;max-width:100%}.personal-plan button{cursor:pointer}.personal-plan button:disabled{opacity:.55;cursor:default}.personal-plan button[aria-pressed=true]{background:var(--ink,var(--td-text-primary));color:var(--paper,var(--td-surface-base))}.personal-plan a{color:inherit;min-height:44px;display:inline-flex;align-items:center;font-size:13px}.personal-plan label{display:grid;gap:7px;font-size:13px;min-width:0}.personal-plan__resume,.personal-plan__picker{padding:24px;background:var(--paper-card,var(--td-surface-raised));border:1px solid var(--line,var(--td-border-default));border-radius:16px}.personal-plan__resume{display:flex;align-items:center;justify-content:space-between;gap:20px;flex-wrap:wrap}.personal-plan__resume small{font-size:11px}.personal-plan__picker{display:grid;grid-template-columns:1fr 1fr auto auto;gap:16px;align-items:end}.personal-plan__picker>h2,.personal-plan__picker>p{grid-column:1/-1}.personal-plan__toolbar,.personal-plan__actions{display:flex;align-items:center;gap:10px;flex-wrap:wrap}.personal-plan__toolbar{align-items:end}.personal-plan__toolbar [role=group]{display:flex;gap:5px}.personal-plan__groups{display:grid;gap:20px}.personal-plan__groups[data-view=board]{grid-template-columns:repeat(auto-fit,minmax(min(300px,100%),1fr));align-items:start}.personal-plan__group{min-width:0}.personal-plan__card{padding:20px;margin-top:12px;border:1px solid var(--line,var(--td-border-default));border-radius:12px;background:var(--paper-card,var(--td-surface-raised))}.personal-plan .personal-plan__blocked{font-weight:600;color:inherit}@media(max-width:720px){.personal-plan__picker{grid-template-columns:1fr}.personal-plan__resume,.personal-plan__picker{padding:18px}.personal-plan__toolbar{align-items:start}.personal-plan__card{padding:16px}}
</style>
