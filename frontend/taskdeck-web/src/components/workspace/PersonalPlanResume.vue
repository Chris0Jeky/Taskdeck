<script setup lang="ts">
import { onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useWorkspacePlanStore } from '../../store/workspacePlanStore'
const plan = useWorkspacePlanStore()
const router = useRouter()
async function resume() {
  const card = plan.plan?.lastWorked
  if (card?.available && await plan.focus(card.boardId, card.cardId))
    await router.push({ path: `/workspace/boards/${card.boardId}/cards/${card.cardId}/thinking`, query: { focus: '1' } })
}
onMounted(() => { void plan.load() })
</script>

<template>
  <section class="plan-resume" aria-label="Personal continuity">
    <p v-if="plan.loading" role="status">Finding your last focus…</p>
    <div v-else-if="plan.error" role="alert">{{ plan.error }} <button type="button" @click="plan.load">Retry personal plan</button></div>
    <template v-else-if="plan.plan?.lastWorked?.available">
      <div><p>LAST WORKED ON · {{ plan.plan.lastWorked.boardName }}</p><h2>{{ plan.plan.lastWorked.title }}</h2></div>
      <button type="button" :disabled="!plan.ready || plan.saving" @click="resume">Resume focus</button>
    </template>
    <p v-else>{{ plan.plan?.lastWorked ? 'Your previous focus is no longer available.' : 'Choose a focus and leave yourself a place to return.' }}</p>
    <RouterLink to="/workspace/plan">Your personal plan</RouterLink>
    <ul v-if="plan.ready && plan.plan?.entries.length" aria-label="Your chosen threads">
      <li v-for="entry in plan.plan.entries.slice(0, 3)" :key="entry.cardId">
        <RouterLink v-if="entry.available" :to="`/workspace/boards/${entry.boardId}/cards/${entry.cardId}/thinking`">{{ entry.title }}</RouterLink><span v-else>Card unavailable</span>
        <small>Planned {{ entry.plannedDate }}<span v-if="entry.isBlocked"> · Blocked</span></small>
      </li>
    </ul>
  </section>
</template>

<style scoped>
.plan-resume{display:flex;align-items:center;gap:20px;flex-wrap:wrap;padding:20px;border:1px solid var(--line,var(--td-border-default));border-radius:14px;background:var(--paper-card,var(--td-surface-raised))}.plan-resume>div:first-child{flex:1}.plan-resume p{font-size:12px;color:var(--ink-muted,var(--td-text-secondary));margin:0 0 6px}.plan-resume h2{font:500 22px/1.3 var(--serif,Georgia,serif);margin:0;overflow-wrap:anywhere}.plan-resume a,.plan-resume button{color:inherit;min-height:44px;display:inline-flex;align-items:center;font-size:13px}
</style>

<style scoped>
.plan-resume ul{width:100%;list-style:none;margin:0;padding:0;border-top:1px solid var(--line,var(--td-border-default))}.plan-resume li{display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap;padding:8px 0}.plan-resume li a{overflow-wrap:anywhere;max-width:100%}.plan-resume small{font-size:11px;color:var(--ink-muted,var(--td-text-secondary))}
</style>
