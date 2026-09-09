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
  </section>
</template>

<style scoped>
.plan-resume{display:flex;align-items:center;gap:20px;flex-wrap:wrap;padding:20px;border:1px solid var(--line,var(--td-border-default));border-radius:14px;background:var(--paper-card,var(--td-surface-raised))}.plan-resume>div:first-child{flex:1}.plan-resume p{font-size:12px;color:var(--ink-muted,var(--td-text-secondary));margin:0 0 6px}.plan-resume h2{font:500 22px/1.3 var(--serif,Georgia,serif);margin:0;overflow-wrap:anywhere}.plan-resume a,.plan-resume button{color:inherit;min-height:44px;display:inline-flex;align-items:center;font-size:13px}
</style>
