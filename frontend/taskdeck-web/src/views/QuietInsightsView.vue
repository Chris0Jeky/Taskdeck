<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import PaperHLBtn from '../components/paper/PaperHLBtn.vue'
import { TdSkeleton } from '../components/ui'
import { workspaceInsightsApi } from '../api/workspaceInsights'
import { useBoardStore } from '../store/boardStore'
import TdDialog from '../components/ui/TdDialog.vue'
import { useUnsavedWorkspaceNavigation } from '../composables/useUnsavedWorkspaceNavigation'
import type { Board } from '../types/board'
import type {
  Insight,
  InsightAction,
  MemoryStatus,
} from '../types/workspaceInsights'

const route = useRoute()
const boardStore = useBoardStore()

const boards = ref<Board[]>([])
const selectedBoardId = ref('')
const boardLoading = ref(false)
const boardError = ref<string | null>(null)
const insights = ref<Insight[]>([])
const loading = ref(false)
const error = ref<string | null>(null)
const analyzing = ref(false)
const initialized = ref(false)
let insightsRequestGeneration = 0
const busyInsightIds = ref(new Set<string>())
const cardErrors = ref<Record<string, string>>({})
const answerNotice = ref<string | null>(null)
const answeringInsightId = ref<string | null>(null)
const answerText = ref('')
const answerStatus = ref<MemoryStatus>('statement')
const answerError = ref<string | null>(null)
const { leaveRequested, decide } = useUnsavedWorkspaceNavigation(() => Boolean(answeringInsightId.value && answerText.value.trim()) || busyInsightIds.value.size > 0)

const selectedBoard = computed(() => boards.value.find((board) => board.id === selectedBoardId.value) ?? null)

function queryBoardId(): string | null {
  const value = route.query.boardId
  if (Array.isArray(value)) return value[0] ?? null
  return typeof value === 'string' ? value : null
}

function errorMessage(value: unknown, fallback: string): string {
  return value instanceof Error && value.message ? value.message : fallback
}

function boardHref(boardId: string): string {
  return `/workspace/boards/${encodeURIComponent(boardId)}`
}

function cardHref(insight: Insight): string {
  if (!insight.cardId) return boardHref(insight.boardId)
  return `${boardHref(insight.boardId)}/cards/${encodeURIComponent(insight.cardId)}/thinking`
}

function formatDate(value: string | null): string {
  if (!value) return 'Not scheduled'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(date)
}

function statusLabel(state: Insight['state']): string {
  return state === 'snoozed' ? 'Snoozed' : state.charAt(0).toUpperCase() + state.slice(1)
}

function evidenceItems(evidence: string): Array<{ label: string; value: string }> {
  return [{ label: 'Evidence', value: evidence }]
}

function actionForState(state: Insight['state']): InsightAction[] {
  if (state === 'available') return ['dismiss', 'snooze', 'mute']
  return ['reopen']
}

function actionLabel(action: InsightAction): string {
  return action.charAt(0).toUpperCase() + action.slice(1)
}

function isBusy(id: string): boolean {
  return busyInsightIds.value.has(id)
}

function setBusy(id: string, value: boolean) {
  const next = new Set(busyInsightIds.value)
  if (value) next.add(id)
  else next.delete(id)
  busyInsightIds.value = next
}

async function loadBoards() {
  boardLoading.value = true
  boardError.value = null
  try {
    // Use the existing store path so board selection has the same ownership
    // and pagination semantics as every other workspace surface.
    await boardStore.fetchBoards()
    boards.value = boardStore.boards.filter((board) => !board.isArchived)
    const requested = queryBoardId()
    if (requested && !boards.value.some(board => board.id === requested)) {
      selectedBoardId.value = ''
      insights.value = []
      boardError.value = 'This board is not available. Open an accessible board to inspect its insights.'
      return
    }
    selectedBoardId.value = boards.value.some((board) => board.id === requested)
      ? requested!
      : boards.value[0]?.id ?? ''
  } catch (value: unknown) {
    boardError.value = errorMessage(value, 'Unable to load your boards.')
  } finally {
    boardLoading.value = false
  }
}

async function loadInsights() {
  if (!selectedBoardId.value) {
    insights.value = []
    return
  }

  const boardId = selectedBoardId.value
  const generation = ++insightsRequestGeneration
  loading.value = true
  error.value = null
  insights.value = []
  try {
    const result = await workspaceInsightsApi.getInsights(boardId)
    if (generation === insightsRequestGeneration && selectedBoardId.value === boardId) {
      insights.value = result
    }
  } catch (value: unknown) {
    if (generation === insightsRequestGeneration && selectedBoardId.value === boardId) {
      error.value = errorMessage(value, 'Unable to load quiet insights.')
    }
  } finally {
    if (generation === insightsRequestGeneration) loading.value = false
  }
}

async function analyzeBoard() {
  if (!selectedBoardId.value || loading.value || analyzing.value || answeringInsightId.value || busyInsightIds.value.size > 0) return
  const boardId = selectedBoardId.value
  const generation = ++insightsRequestGeneration
  analyzing.value = true
  error.value = null
  try {
    const result = await workspaceInsightsApi.analyzeBoard({ boardId })
    if (generation === insightsRequestGeneration && selectedBoardId.value === boardId) {
      insights.value = result
    }
  } catch (value: unknown) {
    if (generation === insightsRequestGeneration && selectedBoardId.value === boardId) {
      error.value = errorMessage(value, 'Unable to analyze this board.')
    }
  } finally {
    // Only one analysis can run; a route/read generation change must still settle it.
    analyzing.value = false
  }
}

async function applyAction(insight: Insight, action: InsightAction) {
  if (analyzing.value || loading.value || isBusy(insight.id) || answeringInsightId.value) return
  const boardId = selectedBoardId.value
  setBusy(insight.id, true)
  cardErrors.value = { ...cardErrors.value, [insight.id]: '' }
  try {
    await workspaceInsightsApi.updateInsight(insight.id, action)
    // A mute/reopen can affect related insight rows, so refresh the family.
    if (selectedBoardId.value === boardId) await loadInsights()
  } catch (value: unknown) {
    cardErrors.value = {
      ...cardErrors.value,
      [insight.id]: errorMessage(value, `Unable to ${action} this insight.`),
    }
  } finally {
    setBusy(insight.id, false)
  }
}

function openAnswer(insight: Insight) {
  if (analyzing.value || loading.value || answeringInsightId.value || busyInsightIds.value.size > 0) return
  answeringInsightId.value = insight.id
  answerText.value = ''
  answerStatus.value = 'statement'
  answerError.value = null
}

function closeAnswer() {
  answeringInsightId.value = null
  answerText.value = ''
  answerError.value = null
}

async function answerInsight(insight: Insight) {
  const text = answerText.value
  if (!text.trim() || isBusy(insight.id)) return

  setBusy(insight.id, true)
  answerError.value = null
  try {
    await workspaceInsightsApi.answerInsight(insight.id, {
      text,
      status: answerStatus.value,
      // This exact string is the question basis the server checked. Keeping
      // it from the displayed insight prevents an answer to stale evidence.
      evidence: insight.evidence,
    })
    // The answer endpoint returns a private memory. Keep the insight's state
    // server-authoritative; recording an answer does not imply a board change.
    answerNotice.value = 'Saved to private memory. The board was not changed.'
    closeAnswer()
    await loadInsights()
  } catch (value: unknown) {
    answerError.value = errorMessage(value, 'Unable to save this answer.')
  } finally {
    setBusy(insight.id, false)
  }
}

function retry() {
  if (boardError.value || !selectedBoardId.value) void loadBoards()
  else void loadInsights()
}

onMounted(async () => {
  await loadBoards()
  initialized.value = true
  if (selectedBoardId.value) await loadInsights()
})

watch(selectedBoardId, (next, previous) => {
  if (initialized.value && next !== previous) void loadInsights()
})
watch(queryBoardId, () => {
  closeAnswer()
  answerNotice.value = null
  if (initialized.value) void loadBoards()
})
</script>

<template>
  <main class="paper-insights" aria-labelledby="quiet-insights-title">
    <header class="paper-insights__hero">
      <div>
        <p class="paper-insights__eyebrow">Workspace signal</p>
        <h1 id="quiet-insights-title">Quiet insights</h1>
        <p class="paper-insights__lede">
          Small, structural observations that help you notice drift before it becomes maintenance.
        </p>
      </div>
      <div class="paper-insights__hero-mark" aria-hidden="true">◎</div>
    </header>

    <section class="paper-insights__panel paper-insights__controls" aria-label="Insight controls">
      <label class="paper-insights__field" for="insights-board-select">
        <span class="paper-insights__label">Board</span>
        <select id="insights-board-select" v-model="selectedBoardId" :disabled="boardLoading || boards.length === 0 || analyzing || loading || Boolean(answeringInsightId) || busyInsightIds.size > 0">
          <option value="" disabled>Select a board</option>
          <option v-for="board in boards" :key="board.id" :value="board.id">{{ board.name }}</option>
        </select>
      </label>
      <div class="paper-insights__control-copy">
        <span v-if="selectedBoard" class="paper-insights__selected-board">{{ selectedBoard.name }}</span>
        <span class="paper-insights__hint">Rule checks are explicit and reviewable.</span>
      </div>
      <PaperHLBtn
        data-action="analyze-insights"
        variant="ember"
        :disabled="!selectedBoardId || analyzing || loading || Boolean(answeringInsightId) || busyInsightIds.size > 0"
        @click="analyzeBoard"
      >
        {{ analyzing ? 'Analyzing…' : 'Analyze now' }}
      </PaperHLBtn>
    </section>

    <p class="paper-insights__trust-note">
      Analysis reads board structure and records private insights. It does not edit cards or columns.
    </p>
    <p v-if="answerNotice" class="paper-insights__saved" role="status">{{ answerNotice }}</p>

    <section v-if="boardLoading" class="paper-insights__state" role="status" aria-live="polite">
      <span class="sr-only">Loading boards…</span>
      <TdSkeleton v-for="n in 3" :key="n" height="14px" :width="`${55 + n * 10}%`" />
    </section>

    <section v-else-if="boardError" class="paper-insights__state paper-insights__state--error" role="alert">
      <p>{{ boardError }}</p>
      <PaperHLBtn variant="ghost" @click="retry">Retry</PaperHLBtn>
    </section>

    <section v-else-if="boards.length === 0" class="paper-insights__state" role="status">
      <span class="paper-insights__state-icon" aria-hidden="true">◇</span>
      <h2>No boards yet</h2>
      <p>Create a board first, then return here when you want a quiet structural check.</p>
      <RouterLink class="paper-insights__link" to="/workspace/boards">Open boards</RouterLink>
    </section>

    <section v-else-if="loading" class="paper-insights__grid" role="status" aria-live="polite">
      <span class="sr-only">Loading insights…</span>
      <article v-for="n in 3" :key="n" class="paper-insights__card paper-insights__card--skeleton">
        <TdSkeleton width="35%" height="11px" />
        <TdSkeleton width="76%" height="20px" />
        <TdSkeleton width="100%" height="12px" />
        <TdSkeleton width="92%" height="12px" />
      </article>
    </section>

    <section v-else-if="error" class="paper-insights__state paper-insights__state--error" role="alert">
      <p>{{ error }}</p>
      <PaperHLBtn variant="ghost" @click="retry">Retry</PaperHLBtn>
    </section>

    <section v-else-if="insights.length === 0" class="paper-insights__state" role="status">
      <span class="paper-insights__state-icon" aria-hidden="true">✦</span>
      <h2>No quiet insights for this board</h2>
      <p>When you are ready, choose Analyze now. Nothing runs in the background.</p>
    </section>

    <section v-else class="paper-insights__grid" aria-label="Quiet insights">
      <article v-for="insight in insights" :key="insight.id" class="paper-insights__card" :class="`paper-insights__card--${insight.state}`">
        <div class="paper-insights__card-topline">
          <span class="paper-insights__rule">{{ insight.rule === 'blocked-next-step' ? 'A way forward' : 'Working knowledge' }}</span>
          <span class="paper-insights__status">{{ statusLabel(insight.state) }}</span>
        </div>
        <h2>{{ insight.title }}</h2>
        <p class="paper-insights__detail">{{ insight.detail }}</p>

        <dl v-if="insight.evidence" class="paper-insights__evidence">
          <div v-for="item in evidenceItems(insight.evidence)" :key="`${item.label}-${item.value}`">
            <dt>{{ item.label }}</dt>
            <dd>{{ item.value }}</dd>
          </div>
        </dl>

        <div class="paper-insights__context">
          <RouterLink class="paper-insights__link" :to="boardHref(insight.boardId)">Open board</RouterLink>
          <RouterLink v-if="insight.cardId" class="paper-insights__link" :to="cardHref(insight)">Open card context</RouterLink>
          <span class="paper-insights__checked">Checked {{ formatDate(insight.checkedAt) }}</span>
        </div>
        <p v-if="insight.snoozeUntil" class="paper-insights__snooze">Snoozed until {{ formatDate(insight.snoozeUntil) }}</p>

        <div class="paper-insights__actions">
          <PaperHLBtn
            v-for="action in actionForState(insight.state)"
            :key="action"
            :data-action="`${action}-insight`"
            variant="ghost"
            :disabled="analyzing || loading || isBusy(insight.id) || Boolean(answeringInsightId)"
            @click="applyAction(insight, action)"
          >
            {{ actionLabel(action) }}
          </PaperHLBtn>
          <PaperHLBtn
            v-if="insight.state === 'available'"
            data-action="answer-insight"
            variant="primary"
            :disabled="analyzing || loading || isBusy(insight.id) || Boolean(answeringInsightId) || busyInsightIds.size > 0"
            @click="openAnswer(insight)"
          >
            Answer privately
          </PaperHLBtn>
        </div>
        <p v-if="cardErrors[insight.id]" class="paper-insights__card-error" role="alert">{{ cardErrors[insight.id] }}</p>
        <form v-if="answeringInsightId === insight.id" class="paper-insights__answer" @submit.prevent="answerInsight(insight)">
          <label :for="`answer-${insight.id}`" class="paper-insights__label">Your private answer</label>
          <textarea :id="`answer-${insight.id}`" v-model="answerText" rows="3" maxlength="8000" :disabled="isBusy(insight.id)" placeholder="Write what you know, suspect, or want to revisit…" />
          <div class="paper-insights__answer-row">
            <label :for="`answer-status-${insight.id}`" class="paper-insights__label">Knowledge status</label>
            <select :id="`answer-status-${insight.id}`" v-model="answerStatus" :disabled="isBusy(insight.id)">
              <option value="statement">Statement</option>
              <option value="assumption">Assumption</option>
              <option value="unknown">Unknown</option>
              <option value="needsReview">Needs review</option>
            </select>
            <PaperHLBtn type="submit" variant="ember" :disabled="!answerText.trim() || isBusy(insight.id)">Save memory</PaperHLBtn>
            <PaperHLBtn type="button" variant="ghost" :disabled="isBusy(insight.id)" @click="closeAnswer">Cancel</PaperHLBtn>
          </div>
          <p class="paper-insights__answer-note">This saves a private memory only; it does not change the board.</p>
          <p v-if="answerError" class="paper-insights__card-error" role="alert">{{ answerError }}</p>
        </form>
      </article>
    </section>
    <TdDialog :open="leaveRequested" title="Leave this answer?" description="Your answer has not been saved. Keep editing or discard the draft before leaving." @close="decide(false)"><template #footer><button type="button" @click="decide(false)">Keep editing</button><button type="button" :disabled="busyInsightIds.size > 0" @click="decide(true)">Discard answer and leave</button></template></TdDialog>
  </main>
</template>

<style scoped>
.paper-insights {
  min-height: 100%;
  max-width: 1180px;
  margin: 0 auto;
  padding: var(--td-space-8, 1.75rem);
  color: var(--td-text-primary, #e5e2e1);
  background: var(--td-surface-base, #131313);
}

.paper-insights__hero,
.paper-insights__controls,
.paper-insights__context,
.paper-insights__card-topline,
.paper-insights__actions,
.paper-insights__answer-row {
  display: flex;
  align-items: center;
}

.paper-insights__hero {
  justify-content: space-between;
  gap: var(--td-space-6, 1.3rem);
  margin-bottom: var(--td-space-6, 1.3rem);
}

.paper-insights__eyebrow,
.paper-insights__rule,
.paper-insights__label,
.paper-insights__status,
.paper-insights__checked,
.paper-insights__snooze {
  font-size: var(--td-font-xs, 0.6875rem);
  letter-spacing: 0.12em;
  text-transform: uppercase;
}

.paper-insights__eyebrow {
  margin: 0 0 var(--td-space-2, 0.4rem);
  color: var(--td-text-ember, #ff4d4d);
}

.paper-insights h1,
.paper-insights h2,
.paper-insights p {
  margin-top: 0;
}

.paper-insights h1 {
  margin-bottom: var(--td-space-2, 0.4rem);
  font-size: clamp(1.9rem, 4vw, 3rem);
  letter-spacing: -0.03em;
}

.paper-insights__lede {
  max-width: 680px;
  margin-bottom: 0;
  color: var(--td-text-secondary, #e4beba);
  font-size: var(--td-font-lg, 1rem);
  line-height: 1.55;
}

.paper-insights__hero-mark {
  color: var(--td-color-ember, #ff4d4d);
  font-size: 3.5rem;
  line-height: 1;
  opacity: 0.85;
}

.paper-insights__panel,
.paper-insights__card,
.paper-insights__state {
  border: 1px solid var(--td-border-default, rgba(91, 64, 62, 0.25));
  border-radius: var(--td-radius-lg, 0.5rem);
  background: var(--td-surface-container-low, #1c1b1b);
  box-shadow: var(--td-shadow-sm, 0 2px 8px rgba(0, 0, 0, 0.3));
}

.paper-insights__controls {
  flex-wrap: wrap;
  gap: var(--td-space-4, 0.8rem);
  padding: var(--td-space-4, 0.8rem);
}

.paper-insights__field {
  display: grid;
  gap: var(--td-space-2, 0.4rem);
  min-width: 220px;
}

.paper-insights__label {
  color: var(--td-text-muted, rgba(229, 226, 225, 0.6));
  font-weight: 600;
}

.paper-insights select,
.paper-insights textarea {
  border: 1px solid var(--td-border-default, rgba(91, 64, 62, 0.25));
  border-radius: var(--td-radius-md, 0.25rem);
  background: var(--td-surface-container, #201f1f);
  color: var(--td-text-primary, #e5e2e1);
  font: inherit;
}

.paper-insights select {
  min-height: 2.5rem;
  padding: 0 var(--td-space-3, 0.6rem);
}

.paper-insights textarea {
  width: 100%;
  min-height: 6rem;
  padding: var(--td-space-3, 0.6rem);
  resize: vertical;
}

.paper-insights select:focus-visible,
.paper-insights textarea:focus-visible,
.paper-insights__link:focus-visible {
  outline: none;
  box-shadow: var(--td-focus-ring, 0 0 0 2px #ff5352);
}

.paper-insights__control-copy {
  display: grid;
  gap: var(--td-space-1, 0.2rem);
  flex: 1;
  min-width: 180px;
}

.paper-insights__selected-board {
  font-weight: 650;
}

.paper-insights__hint,
.paper-insights__trust-note,
.paper-insights__answer-note {
  color: var(--td-text-muted, rgba(229, 226, 225, 0.6));
  font-size: var(--td-font-sm, 0.75rem);
}

.paper-insights__trust-note {
  margin: var(--td-space-3, 0.6rem) 0 var(--td-space-6, 1.3rem);
}

.paper-insights__state {
  display: grid;
  justify-items: center;
  gap: var(--td-space-3, 0.6rem);
  padding: var(--td-space-10, 2.25rem) var(--td-space-6, 1.3rem);
  text-align: center;
}

.paper-insights__state p {
  max-width: 520px;
  margin-bottom: var(--td-space-2, 0.4rem);
  color: var(--td-text-secondary, #e4beba);
}

.paper-insights__state h2 {
  margin-bottom: 0;
  font-size: var(--td-font-xl, 1.25rem);
}

.paper-insights__state-icon {
  color: var(--td-color-ember, #ff4d4d);
  font-size: 2rem;
}

.paper-insights__state--error {
  border-color: var(--td-color-error, #ff4d4d);
  background: var(--td-color-error-light, rgba(255, 77, 77, 0.15));
}

.paper-insights__state--error p,
.paper-insights__card-error {
  color: var(--td-color-error, #ff4d4d);
}

.paper-insights__grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 340px), 1fr));
  gap: var(--td-space-5, 1.1rem);
}

.paper-insights__card {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-4, 0.8rem);
  padding: var(--td-space-5, 1.1rem);
  border-top: 3px solid var(--td-color-primary, #ffb3ae);
}

.paper-insights__card--dismissed,
.paper-insights__card--muted {
  opacity: 0.72;
}

.paper-insights__card--resolved {
  border-top-color: var(--td-color-success, #4ade80);
}

.paper-insights__card--snoozed {
  border-top-color: var(--td-color-warning, #fbbf24);
}

.paper-insights__card-topline {
  justify-content: space-between;
  gap: var(--td-space-3, 0.6rem);
}

.paper-insights__rule {
  color: var(--td-text-ember, #ff4d4d);
  font-family: var(--td-font-mono, ui-monospace, monospace);
}

.paper-insights__status {
  padding: 0.25rem 0.45rem;
  border: 1px solid currentColor;
  border-radius: var(--td-radius-sm, 0.125rem);
  color: var(--td-text-muted, rgba(229, 226, 225, 0.6));
  white-space: nowrap;
}

.paper-insights__card h2 {
  margin-bottom: 0;
  font-size: var(--td-font-xl, 1.25rem);
  line-height: 1.25;
}

.paper-insights__detail {
  margin-bottom: 0;
  color: var(--td-text-secondary, #e4beba);
  line-height: 1.55;
}

.paper-insights__evidence {
  display: grid;
  gap: var(--td-space-2, 0.4rem);
  margin: 0;
  padding: var(--td-space-3, 0.6rem);
  border-radius: var(--td-radius-md, 0.25rem);
  background: var(--td-surface-container, #201f1f);
}

.paper-insights__evidence div {
  display: grid;
  grid-template-columns: 5rem 1fr;
  gap: var(--td-space-2, 0.4rem);
}

.paper-insights__evidence dt {
  color: var(--td-text-muted, rgba(229, 226, 225, 0.6));
  font-size: var(--td-font-sm, 0.75rem);
}

.paper-insights__evidence dd {
  margin: 0;
  font-size: var(--td-font-sm, 0.75rem);
  line-height: 1.45;
}

.paper-insights__context {
  flex-wrap: wrap;
  gap: var(--td-space-3, 0.6rem);
  padding-top: var(--td-space-2, 0.4rem);
  border-top: 1px solid var(--td-border-ghost, rgba(91, 64, 62, 0.15));
}

.paper-insights__link {
  color: var(--td-color-primary, #ffb3ae);
  font-size: var(--td-font-sm, 0.75rem);
  font-weight: 650;
  text-decoration: none;
}

.paper-insights__link:hover {
  color: var(--td-color-primary-hover, #ff5352);
  text-decoration: underline;
}

.paper-insights__checked {
  margin-left: auto;
  color: var(--td-text-tertiary, rgba(229, 226, 225, 0.4));
  letter-spacing: 0.04em;
  text-transform: none;
}

.paper-insights__snooze {
  margin-bottom: 0;
  color: var(--td-color-warning, #fbbf24);
  letter-spacing: 0.04em;
  text-transform: none;
}

.paper-insights__actions {
  flex-wrap: wrap;
  gap: var(--td-space-2, 0.4rem);
}

.paper-insights__card-error,
.paper-insights__saved {
  margin-bottom: 0;
  font-size: var(--td-font-sm, 0.75rem);
}

.paper-insights__saved {
  color: var(--td-color-success, #4ade80);
}

.paper-insights__answer {
  display: grid;
  gap: var(--td-space-3, 0.6rem);
  padding-top: var(--td-space-4, 0.8rem);
  border-top: 1px solid var(--td-border-default, rgba(91, 64, 62, 0.25));
}

.paper-insights__answer-row {
  flex-wrap: wrap;
  gap: var(--td-space-2, 0.4rem);
}

.paper-insights__answer-row .paper-insights__label {
  margin-right: auto;
}

.paper-insights__answer-note {
  margin-bottom: 0;
}

.paper-insights__card--skeleton {
  align-items: flex-start;
}

@media (max-width: 640px) {
  .paper-insights {
    padding: var(--td-space-4, 0.8rem);
  }

  .paper-insights__hero-mark {
    display: none;
  }

  .paper-insights__controls > .pbtn {
    width: 100%;
  }

  .paper-insights__checked {
    width: 100%;
    margin-left: 0;
  }
}
</style>
