<script setup lang="ts">
import { computed, defineAsyncComponent, ref, watch } from 'vue'
import { useWorkspaceLayoutStore } from '../../store/workspaceLayoutStore'
import { useFeatureFlagStore } from '../../store/featureFlagStore'
import { useOverhaulHome } from '../../composables/useOverhaulHome'
import { useUnsavedWorkspaceNavigation } from '../../composables/useUnsavedWorkspaceNavigation'
import PersonalPlanResume from '../../components/workspace/PersonalPlanResume.vue'
import TdDialog from '../../components/ui/TdDialog.vue'

const AutomationChatView = defineAsyncComponent(() => import('../AutomationChatView.vue'))
const layout = useWorkspaceLayoutStore()
const flags = useFeatureFlagStore()
const { workspace, boards, agenda, captureText, captureBusy, captureError, captureSaved, saveCapture, refresh } = useOverhaulHome()
const { leaveRequested, decide } = useUnsavedWorkspaceNavigation(() => Boolean(captureText.value.trim()) || captureBusy.value)
const companionVisited = ref(false)
const chatAvailable = computed(() => flags.isEnabled('newAutomation') || workspace.mode === 'workbench')
watch(() => layout.experience, value => {
  if (value === 'companion') companionVisited.value = true
}, { immediate: true })

const introduction = computed(() => {
  if (layout.experience === 'companion') return { title: 'Think it through. Make it real.', subtitle: 'A conversation connected to your projects, with room to inspect every proposed change.' }
  if (layout.experience === 'unified') return { title: 'One workspace. Your pace.', subtitle: 'Keep your work, context and unfinished thinking together. Choose how much structure to see.' }
  return { title: 'A little room to move forward.', subtitle: 'Pick up a thread, make space for a thought, or return to the work already taking shape.' }
})
const firstBoard = computed(() => boards.value[0])
</script>

<template>
  <div class="overhaul-home" :data-experience="layout.experience" :data-detail="layout.presentation">
    <header class="overhaul-home__hero">
      <div>
        <p class="overhaul-home__eyebrow">YOUR WORK, AT YOUR PACE</p>
        <h1>{{ introduction.title }}</h1>
        <p class="overhaul-home__lede">{{ introduction.subtitle }}</p>
      </div>
      <RouterLink class="overhaul-home__quiet-link" to="/workspace/experiences">Compare experiences ↗</RouterLink>
    </header>

    <nav class="overhaul-home__destinations" aria-label="Workspace destinations">
      <RouterLink to="/workspace/boards">Your projects</RouterLink>
      <RouterLink to="/workspace/insights">Quiet insights</RouterLink>
      <RouterLink to="/workspace/memory">Memory &amp; questions</RouterLink>
      <RouterLink to="/workspace/inbox">Collected thoughts</RouterLink>
      <RouterLink v-if="chatAvailable" to="/workspace/review">Review changes</RouterLink>
    </nav>

    <div v-if="workspace.homeError || workspace.todayError" class="overhaul-home__notice" role="alert">
      <p>{{ workspace.homeError || workspace.todayError }}</p>
      <p>Some workspace information is unavailable. Any information still shown may be out of date.</p>
      <button type="button" :disabled="workspace.homeLoading || workspace.todayLoading" @click="refresh">Refresh workspace</button>
    </div>

    <!-- Keep the conversation mounted when switching experience so a composed reply survives. -->
    <section v-if="companionVisited && chatAvailable" v-show="layout.experience === 'companion'" class="overhaul-home__conversation" aria-label="Workspace companion">
      <AutomationChatView />
    </section>
    <p v-if="layout.experience === 'companion' && !chatAvailable" class="overhaul-home__notice">Chat is unavailable with the current workspace settings. Your projects, capture and memory remain available below.</p>

    <PersonalPlanResume />

    <div class="overhaul-home__desk">
      <section class="overhaul-home__continuity">
        <p class="overhaul-home__eyebrow">{{ layout.experience === 'unified' ? 'YOUR WORKSPACE' : 'A PLACE TO START' }}</p>
        <template v-if="agenda.length">
          <h2>{{ agenda[0]!.title }}</h2>
          <p>{{ agenda[0]!.boardName }}<span v-if="agenda[0]!.blockReason"> · {{ agenda[0]!.blockReason }}</span></p>
          <RouterLink class="overhaul-home__primary" :to="`/workspace/boards/${agenda[0]!.boardId}/cards/${agenda[0]!.cardId}/thinking`">Open thinking deck <span aria-hidden="true">↗</span></RouterLink>
          <RouterLink class="overhaul-home__quiet-link" :to="`/workspace/boards/${agenda[0]!.boardId}`">Continue on the board</RouterLink>
        </template>
        <template v-else-if="firstBoard">
          <h2>{{ firstBoard.name }}</h2>
          <p>{{ firstBoard.description || 'Your next step can start here.' }}</p>
          <RouterLink class="overhaul-home__primary" :to="`/workspace/boards/${firstBoard.id}`">Continue in your project <span aria-hidden="true">↗</span></RouterLink>
        </template>
        <template v-else-if="workspace.homeLoading">
          <p role="status">Finding your place…</p>
        </template>
        <template v-else>
          <h2>Start with something that matters.</h2>
          <p>A project gives your thoughts somewhere to become work. A name is enough to begin.</p>
          <RouterLink class="overhaul-home__primary" to="/workspace/boards">Create your first project <span aria-hidden="true">↗</span></RouterLink>
        </template>
      </section>

      <form class="overhaul-home__capture" @submit.prevent="saveCapture">
        <label for="overhaul-thought" class="overhaul-home__eyebrow">LEAVE A THOUGHT HERE</label>
        <textarea id="overhaul-thought" v-model="captureText" rows="4" maxlength="10000" placeholder="A thought, a question, a loose end…" />
        <div class="overhaul-home__capture-footer">
          <span>Keep the original. Decide what it becomes later.</span>
          <button class="overhaul-home__primary" type="submit" :disabled="captureBusy || !captureText.trim()">{{ captureBusy ? 'Saving…' : 'Save to Inbox' }}</button>
        </div>
        <p v-if="captureError" role="alert">{{ captureError }}</p>
        <p v-if="captureSaved" role="status">Saved. <RouterLink to="/workspace/inbox">Open your collected thoughts</RouterLink>.</p>
      </form>
    </div>

    <section class="overhaul-home__projects">
      <div class="overhaul-home__section-heading"><h2>Your spaces</h2><RouterLink to="/workspace/boards">All projects ↗</RouterLink></div>
      <div class="overhaul-home__project-grid">
        <article v-for="board in boards" :key="board.id" class="overhaul-home__project">
          <span class="overhaul-home__project-icon" aria-hidden="true">▧</span>
          <h3><RouterLink :to="`/workspace/boards/${board.id}`">{{ board.name }}</RouterLink></h3>
          <p>{{ board.description || 'Room for what comes next.' }}</p>
          <div class="overhaul-home__project-actions">
            <RouterLink :to="{ path: '/workspace/insights', query: { boardId: board.id } }">Insights</RouterLink>
            <RouterLink :to="{ path: '/workspace/memory', query: { boardId: board.id } }">Memory</RouterLink>
            <RouterLink v-if="chatAvailable" :to="{ path: '/workspace/automations/chat', query: { boardId: board.id } }">Think with me</RouterLink>
          </div>
        </article>
        <RouterLink class="overhaul-home__new-project" to="/workspace/boards"><span aria-hidden="true">＋</span><span>Make room for a project</span></RouterLink>
      </div>
    </section>

    <section v-if="agenda.length" class="overhaul-home__agenda">
      <div class="overhaul-home__section-heading"><h2>A few threads to pick up</h2><RouterLink to="/workspace/today">Open Today ↗</RouterLink></div>
      <article v-for="card in agenda" :key="card.cardId" class="overhaul-home__thread">
        <span aria-hidden="true">{{ card.blockReason ? '◇' : '○' }}</span>
        <div><RouterLink :to="`/workspace/boards/${card.boardId}/cards/${card.cardId}/thinking`">{{ card.title }}</RouterLink><small>{{ card.boardName }}</small></div>
        <span v-if="layout.presentation !== 'zen' && card.blockReason" class="overhaul-home__tag">{{ card.blockReason }}</span>
      </article>
    </section>
    <p v-if="workspace.homeSummary" class="overhaul-home__receipt">{{ workspace.homeSummary.workload.proposalsPendingReview }} proposals awaiting review · Your chosen experience shares the same work.</p>
    <TdDialog :open="leaveRequested" title="Leave this thought?" description="Your thought has not been saved. Keep editing or discard it before leaving." @close="decide(false)"><template #footer><button type="button" @click="decide(false)">Keep editing</button><button type="button" :disabled="captureBusy" @click="decide(true)">Discard thought and leave</button></template></TdDialog>
  </div>
</template>

<style scoped>
.overhaul-home{color:var(--ink,var(--td-text-primary));font-family:var(--sans,system-ui,sans-serif);max-width:1440px;margin:auto;padding:clamp(4px,1.5vw,24px);display:grid;gap:32px}
.overhaul-home__hero{display:flex;align-items:flex-start;justify-content:space-between;gap:24px}
.overhaul-home__eyebrow{font-size:11px;font-weight:650;letter-spacing:.16em;color:var(--ink-muted,var(--td-text-secondary));margin:0 0 16px}
h1{font-family:var(--serif,Georgia,serif);font-size:clamp(30px,3.3vw,52px);font-weight:450;line-height:1.12;letter-spacing:-.025em;margin:0 0 16px;max-width:850px}h2{font-family:var(--serif,Georgia,serif);font-size:27px;line-height:1.25;font-weight:500;margin:0 0 12px}h3{font-size:17px;margin:14px 0 8px}p{line-height:1.65}.overhaul-home__lede{max-width:670px;color:var(--ink-muted,var(--td-text-secondary));margin:0;font-size:15px}
.overhaul-home a{text-decoration:none}.overhaul-home a:hover{text-decoration:underline}.overhaul-home__quiet-link{font-size:13px;white-space:nowrap;color:inherit}.overhaul-home__destinations{display:flex;flex-wrap:wrap;gap:8px 24px;padding-bottom:18px;border-bottom:1px solid var(--line,var(--td-border-default))}.overhaul-home__destinations a{font-size:13px;color:inherit;min-height:32px;display:flex;align-items:center}
.overhaul-home__desk{display:grid;grid-template-columns:1.15fr 1fr;gap:24px}.overhaul-home__continuity{background:var(--paper-muted,var(--td-surface-raised));border:1px solid var(--line,var(--td-border-default));border-radius:18px;padding:30px}.overhaul-home__continuity p:not(.overhaul-home__eyebrow){font-size:14px;color:var(--ink-muted,var(--td-text-secondary))}.overhaul-home__continuity .overhaul-home__quiet-link{display:inline-block;margin:14px 0 0 14px}.overhaul-home__primary{display:inline-flex;gap:20px;align-items:center;justify-content:center;border:1px solid var(--ink,var(--td-border-default));border-radius:9px;background:var(--ink,var(--td-text-primary));color:var(--paper,var(--td-surface-base));padding:12px 17px;font-size:13px;font-weight:600;min-height:44px;cursor:pointer}.overhaul-home__primary:disabled{opacity:.55;cursor:default}
.overhaul-home__capture{display:flex;flex-direction:column;padding:26px;background:var(--paper-card,var(--td-surface-raised));border:1px solid var(--line,var(--td-border-default));border-radius:18px}.overhaul-home__capture textarea{resize:vertical;width:100%;min-height:105px;background:transparent;border:0;border-bottom:1px solid var(--line,var(--td-border-default));padding:10px 0;color:inherit;font:inherit}.overhaul-home__capture-footer{display:flex;align-items:center;justify-content:space-between;gap:15px;margin-top:14px}.overhaul-home__capture-footer span{font-size:12px;color:var(--ink-muted,var(--td-text-secondary));max-width:190px}.overhaul-home__capture p{font-size:13px;margin-bottom:0}
.overhaul-home__section-heading{display:flex;align-items:center;justify-content:space-between;gap:16px;margin-bottom:16px}.overhaul-home__section-heading h2{font-size:23px;margin:0}.overhaul-home__section-heading>a{color:inherit;font-size:12px}.overhaul-home__project-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(230px,100%),1fr));gap:15px}.overhaul-home__project{border:1px solid var(--line,var(--td-border-default));background:var(--paper-card,var(--td-surface-raised));border-radius:14px;padding:24px;min-width:0}.overhaul-home__project h3 a{color:inherit}.overhaul-home__project p{font-size:13px;color:var(--ink-muted,var(--td-text-secondary));overflow-wrap:anywhere}.overhaul-home__project-icon{font-size:24px;color:var(--olive,var(--ink))}.overhaul-home__project-actions{display:flex;flex-wrap:wrap;gap:15px;margin-top:20px}.overhaul-home__project-actions a{font-size:12px;color:var(--ink,var(--td-text-primary))}.overhaul-home__new-project{border:1px dashed var(--line,var(--td-border-default));border-radius:14px;display:flex;flex-direction:column;gap:14px;align-items:center;justify-content:center;min-height:180px;color:var(--ink-muted,var(--td-text-secondary));font-size:13px}.overhaul-home__new-project>span:first-child{font-size:28px}
.overhaul-home__thread{display:flex;align-items:center;gap:17px;padding:16px 5px;border-bottom:1px solid var(--line,var(--td-border-default));font-size:14px}.overhaul-home__thread>div{flex:1;min-width:0}.overhaul-home__thread a{color:inherit;overflow-wrap:anywhere}.overhaul-home__thread small{display:block;font-size:11px;color:var(--ink-muted,var(--td-text-secondary));margin-top:4px}.overhaul-home__tag{font-size:11px;max-width:200px;color:var(--ink-muted,var(--td-text-secondary))}.overhaul-home__receipt{font-size:11px;color:var(--ink-muted,var(--td-text-secondary));margin:0}.overhaul-home__notice{padding:16px;border:1px solid var(--line,var(--td-border-default));border-radius:10px;font-size:13px}.overhaul-home__notice p{margin:0 0 8px}.overhaul-home__notice button{font:inherit;text-decoration:underline;cursor:pointer}.overhaul-home__conversation{min-width:0}.overhaul-home[data-detail=control]{gap:20px}.overhaul-home[data-detail=control] h1,.overhaul-home[data-detail=control] h2{font-family:inherit}.overhaul-home[data-detail=control] h1{font-size:34px}.overhaul-home[data-detail=control] .overhaul-home__continuity,.overhaul-home[data-detail=control] .overhaul-home__capture{padding:20px;border-radius:8px}.overhaul-home[data-detail=zen]{max-width:1100px;gap:40px}.overhaul-home[data-experience=companion] .overhaul-home__desk{grid-template-columns:1fr 1.4fr}
@media(max-width:720px){.overhaul-home{gap:24px}.overhaul-home__hero{flex-direction:column;gap:16px}.overhaul-home__desk,.overhaul-home[data-experience=companion] .overhaul-home__desk{grid-template-columns:1fr}.overhaul-home__continuity,.overhaul-home__capture{padding:20px}.overhaul-home__capture-footer{align-items:flex-start;flex-direction:column}.overhaul-home__destinations{gap:8px 16px}.overhaul-home__tag{display:none}.overhaul-home__hero>.overhaul-home__quiet-link{white-space:normal}}
</style>
