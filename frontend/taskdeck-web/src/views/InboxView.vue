<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import WorkspaceHelpCallout from '../components/workspace/WorkspaceHelpCallout.vue'
import { useCaptureStore } from '../store/captureStore'
import type { CaptureItem, CaptureItemSummary, CaptureSourceValue, CaptureStatusValue } from '../types/capture'
import { registerEscapeHandler } from '../composables/useEscapeStack'
import { normalizeBoardIdQueryParam } from '../utils/navigation'

const captureStore = useCaptureStore()
const router = useRouter()
const route = useRoute()
const selectedItemId = ref<string | null>(null)
const hashLoadFailedItemId = ref<string | null>(null)
const activeItemIndex = ref(0)
const listContainer = ref<HTMLElement | null>(null)

const items = computed(() => captureStore.items)
const activeDescendantId = computed(() => {
  if (items.value.length === 0) {
    return undefined
  }

  return `td-inbox-option-${activeItemIndex.value}`
})
const selectedItem = computed(() => {
  if (!selectedItemId.value) {
    return null
  }

  return captureStore.detailById[selectedItemId.value] ?? null
})
const activeBoardId = computed(() => normalizeBoardIdQueryParam(route.query.boardId))

function getCaptureIdFromHash(hash: string): string | null {
  if (!hash.startsWith('#capture-')) {
    return null
  }

  const rawId = hash.slice('#capture-'.length).trim()
  if (!rawId) {
    return null
  }

  try {
    return decodeURIComponent(rawId)
  } catch {
    return null
  }
}

function statusLabel(status: CaptureStatusValue): string {
  if (status === 0 || status === 'New') return 'New'
  if (status === 1 || status === 'Triaging') return 'Triaging'
  if (status === 2 || status === 'Triaged') return 'Triaged'
  if (status === 3 || status === 'ProposalCreated') return 'Ready for review'
  if (status === 4 || status === 'Converted') return 'Applied to board'
  if (status === 5 || status === 'Ignored') return 'Ignored'
  if (status === 6 || status === 'Failed') return 'Failed'
  return String(status)
}

function sourceLabel(source: CaptureSourceValue): string {
  if (source === 0 || source === 'Typed') return 'Typed'
  if (source === 1 || source === 'Paste') return 'Paste'
  if (source === 2 || source === 'TranscriptPaste') return 'Transcript'
  if (source === 3 || source === 'Import') return 'Import'
  if (source === 4 || source === 'Voice') return 'Voice'
  if (source === 5 || source === 'MeetingIntegration') return 'Meeting'
  return String(source)
}

async function loadInbox() {
  try {
    await captureStore.fetchItems({
      limit: 200,
      ...(activeBoardId.value ? { boardId: activeBoardId.value } : {}),
    })
  } catch {
    // Store handles toast + error state.
  }

  await openItemFromHash()
}

async function openItemFromList(item: CaptureItemSummary, index: number) {
  hashLoadFailedItemId.value = null
  await clearCaptureHash()
  await selectItemById(item.id, { preferredIndex: index })
}

type SelectItemOptions = {
  preferredIndex?: number
  preloadedDetail?: CaptureItem
  cacheSummary?: boolean
}

function isHttpNotFound(error: unknown): boolean {
  const candidate = error as { response?: { status?: number; data?: { errorCode?: string } } } | null
  return candidate?.response?.status === 404 || candidate?.response?.data?.errorCode === 'NotFound'
}

function primeSelection(itemId: string, preferredIndex?: number) {
  if (preferredIndex !== undefined) {
    setActiveIndex(preferredIndex)
  } else {
    const matchingIndex = items.value.findIndex((item) => item.id === itemId)
    if (matchingIndex >= 0) {
      setActiveIndex(matchingIndex)
    }
  }

  selectedItemId.value = itemId
}

async function selectItemById(itemId: string, options: SelectItemOptions = {}): Promise<boolean> {
  const {
    preferredIndex,
    preloadedDetail,
    cacheSummary = true,
  } = options

  primeSelection(itemId, preferredIndex)
  hashLoadFailedItemId.value = null
  try {
    if (preloadedDetail) {
      captureStore.cacheDetail(preloadedDetail, cacheSummary)
      return true
    }

    await captureStore.fetchDetail(itemId)
    return true
  } catch {
    if (selectedItemId.value === itemId) {
      selectedItemId.value = null
    }

    return false
  }
}

async function openBoardScopedHashItem(captureId: string): Promise<void> {
  try {
    hashLoadFailedItemId.value = null
    const detail = await captureStore.peekDetail(captureId, {
      forceRefresh: true,
      recordError: false,
      showToast: false,
    })
    if (getCaptureIdFromHash(route.hash) !== captureId) {
      return
    }

    if (normalizeBoardIdQueryParam(detail.boardId) !== activeBoardId.value) {
      selectedItemId.value = null
      await clearCaptureHash()
      return
    }

    await selectItemById(captureId, {
      preloadedDetail: detail,
      cacheSummary: false,
    })
    return
  } catch (error) {
    if (getCaptureIdFromHash(route.hash) !== captureId) {
      return
    }

    if (isHttpNotFound(error)) {
      selectedItemId.value = null
      hashLoadFailedItemId.value = null
      await clearCaptureHash()
      return
    }

    selectedItemId.value = null
    hashLoadFailedItemId.value = captureId
  }
}

async function openItemFromHash() {
  const captureId = getCaptureIdFromHash(route.hash)
  if (!captureId) {
    hashLoadFailedItemId.value = null
    return
  }

  if (selectedItemId.value === captureId && selectedItem.value) {
    if (!activeBoardId.value || normalizeBoardIdQueryParam(selectedItem.value.boardId) === activeBoardId.value) {
      hashLoadFailedItemId.value = null
      return
    }
  }

  if (activeBoardId.value) {
    await openBoardScopedHashItem(captureId)
    return
  }

  const opened = await selectItemById(captureId)
  if (!opened) {
    await clearCaptureHash()
  }
}

async function clearCaptureHash() {
  if (!getCaptureIdFromHash(route.hash)) {
    return
  }

  hashLoadFailedItemId.value = null
  await router.replace({
    name: 'workspace-inbox',
    query: route.query,
  })
}

async function closeDetail() {
  selectedItemId.value = null
  hashLoadFailedItemId.value = null
  await clearCaptureHash()
}

function setActiveIndex(index: number) {
  if (index < 0 || index >= items.value.length) {
    return
  }

  activeItemIndex.value = index
}

function scrollActiveItemIntoView() {
  if (!listContainer.value) {
    return
  }

  const targetRow = listContainer.value.querySelector<HTMLElement>(
    `[data-inbox-index="${activeItemIndex.value}"]`)
  targetRow?.scrollIntoView?.({ block: 'nearest' })
}

async function openActiveItem() {
  const target = items.value[activeItemIndex.value]
  if (!target) {
    return
  }

  await openItemFromList(target, activeItemIndex.value)
}

async function handleKeydown(event: KeyboardEvent) {
  if (items.value.length === 0) {
    return
  }

  if (event.key === 'ArrowDown') {
    event.preventDefault()
    activeItemIndex.value = (activeItemIndex.value + 1) % items.value.length
    return
  }

  if (event.key === 'ArrowUp') {
    event.preventDefault()
    activeItemIndex.value = (activeItemIndex.value - 1 + items.value.length) % items.value.length
    return
  }

  if (event.key === 'Enter') {
    event.preventDefault()
    await openActiveItem()
  }
}

async function ignoreSelected() {
  if (!selectedItemId.value) {
    return
  }

  try {
    await captureStore.ignoreItem(selectedItemId.value)
  } catch {
    // Store handles toast + error state.
  }
}

async function cancelSelected() {
  if (!selectedItemId.value) {
    return
  }

  try {
    await captureStore.cancelItem(selectedItemId.value)
  } catch {
    // Store handles toast + error state.
  }
}

async function triageSelected() {
  if (!selectedItemId.value) {
    return
  }

  try {
    await captureStore.triageItem(selectedItemId.value)
  } catch {
    // Store handles toast + error state.
  }
}

async function refreshSelectedDetail() {
  if (!selectedItemId.value) {
    return
  }

  try {
    await captureStore.fetchDetail(selectedItemId.value, { forceRefresh: true })
  } catch {
    // Store handles toast + error state.
  }
}

function canMutateSelection(status: CaptureStatusValue | undefined): boolean {
  if (status === undefined) {
    return false
  }

  return status === 0 ||
    status === 'New' ||
    status === 6 ||
    status === 'Failed'
}

const canTriageSelection = canMutateSelection

function triageButtonLabel(status: CaptureStatusValue | undefined): string {
  if (status === undefined) {
    return 'Start Triage'
  }

  const label = statusLabel(status)
  if (label === 'Triaging') {
    return 'Triaging...'
  }

  if (label === 'Triaged' || label === 'Proposal Created') {
    return 'Triage Complete'
  }

  if (label === 'Converted') {
    return 'Converted'
  }

  return 'Start Triage'
}

function reviewRoute(proposalId?: string, boardId?: string | null) {
  const effectiveBoardId = boardId ?? activeBoardId.value
  return {
    name: 'workspace-review',
    query: effectiveBoardId ? { boardId: effectiveBoardId } : undefined,
    hash: proposalId ? `#proposal-${encodeURIComponent(proposalId)}` : undefined,
  }
}

function openProposal(proposalId: string): void {
  void router.push(reviewRoute(proposalId, selectedItem.value?.boardId ?? null))
}

function openReview(): void {
  void router.push(reviewRoute())
}

function openRoute(path: string): void {
  void router.push(path)
}

watch(items, (nextItems) => {
  if (nextItems.length === 0) {
    activeItemIndex.value = 0
    return
  }

  if (selectedItemId.value) {
    const selectedIndex = nextItems.findIndex((item) => item.id === selectedItemId.value)
    if (selectedIndex >= 0) {
      activeItemIndex.value = selectedIndex
      return
    }
  }

  if (activeItemIndex.value >= nextItems.length) {
    activeItemIndex.value = nextItems.length - 1
  }
})

watch(activeItemIndex, async () => {
  await nextTick()
  scrollActiveItemIntoView()
})

watch(activeBoardId, () => {
  selectedItemId.value = null
  activeItemIndex.value = 0
  void loadInbox()
})

watch(
  () => route.hash,
  () => {
    void openItemFromHash()
  },
)

watch(selectedItemId, (itemId, _, onCleanup) => {
  if (!itemId) {
    return
  }

  const unregister = registerEscapeHandler(closeDetail)
  onCleanup(() => {
    unregister()
  })
})

onMounted(() => {
  void loadInbox()
})
</script>

<template>
  <div class="td-inbox">
    <header class="td-inbox__header">
      <div>
        <h1 class="td-page-title">Inbox</h1>
        <p class="td-inbox__subtitle">Capture rough notes and turn them into reviewable proposed work.</p>
        <p v-if="activeBoardId" class="td-inbox__board-context">
          Showing capture items linked to board {{ activeBoardId }}.
        </p>
      </div>
      <button class="td-btn td-btn--secondary" @click="loadInbox" :disabled="captureStore.loadingList">
        {{ captureStore.loadingList ? 'Refreshing...' : 'Refresh' }}
      </button>
    </header>

    <WorkspaceHelpCallout
      topic="inbox"
      title="What is Inbox for?"
      description="Inbox is where Taskdeck prepares a proposed change from your note, then sends it to Review before anything reaches a board."
    >
      <template #actions>
        <button class="td-btn td-btn--secondary td-btn--sm" @click="openRoute('/workspace/home')">Open Home</button>
        <button class="td-btn td-btn--secondary td-btn--sm" @click="openReview">Open Review</button>
      </template>
    </WorkspaceHelpCallout>

    <div class="td-inbox__layout">
      <section class="td-inbox__list-panel">
        <div class="td-inbox__list-header">
          <h2>Items</h2>
          <span class="td-inbox__count">{{ items.length }}</span>
        </div>

        <div
          ref="listContainer"
          class="td-inbox__list"
          tabindex="0"
          role="listbox"
          aria-label="Inbox items"
          :aria-activedescendant="activeDescendantId"
          @keydown="handleKeydown"
        >
          <div v-if="captureStore.loadingList" class="td-placeholder">Loading inbox items...</div>
          <div v-else-if="captureStore.listError" class="td-alert td-alert--error">{{ captureStore.listError }}</div>
          <div v-else-if="!captureStore.hasItems" class="td-placeholder td-placeholder--empty-state">
            <h3>No capture items yet</h3>
            <p>Start from Home or Today when you want to drop in fresh notes or transcripts. Review will light up once triage creates proposals.</p>
            <div class="td-placeholder__actions">
              <button class="td-btn td-btn--primary td-btn--sm" @click="openRoute('/workspace/home')">Open Home</button>
              <button class="td-btn td-btn--secondary td-btn--sm" @click="openRoute('/workspace/today')">Open Today</button>
              <button class="td-btn td-btn--secondary td-btn--sm" @click="openReview">Open Review</button>
            </div>
          </div>

          <div
            v-for="(item, index) in items"
            :key="item.id"
            :id="`td-inbox-option-${index}`"
            :data-inbox-index="index"
            :class="[
              'td-inbox-row',
              index === activeItemIndex ? 'td-inbox-row--active' : '',
              selectedItemId === item.id ? 'td-inbox-row--selected' : ''
            ]"
            role="option"
            :aria-selected="selectedItemId === item.id"
            @mouseenter="setActiveIndex(index)"
            @click="openItemFromList(item, index)"
          >
            <div class="td-inbox-row__head">
              <span class="td-status-chip">{{ statusLabel(item.status) }}</span>
              <span class="td-meta-chip">{{ sourceLabel(item.source) }}</span>
            </div>
            <p class="td-inbox-row__excerpt">{{ item.textExcerpt }}</p>
            <p class="td-inbox-row__meta">{{ new Date(item.createdAt).toLocaleString() }}</p>
          </div>
        </div>
      </section>

      <section class="td-inbox__detail-panel">
        <div
          v-if="hashLoadFailedItemId && !selectedItemId"
          class="td-placeholder td-placeholder--detail"
          role="alert"
          aria-live="assertive"
        >
          Unable to load capture detail.
        </div>
        <div v-else-if="!selectedItemId" class="td-placeholder td-placeholder--detail">
          Select an item to inspect the captured text and decide whether to triage, ignore, or cancel it.
        </div>
        <div
          v-else-if="captureStore.loadingDetail && !selectedItem"
          class="td-placeholder td-placeholder--detail-loading"
        >
          Loading detail...
        </div>
        <div v-else-if="!selectedItem" class="td-placeholder td-placeholder--detail">
          Unable to load capture detail.
        </div>

        <article v-else class="td-inbox-detail">
          <header class="td-inbox-detail__header">
            <div>
              <h2>Capture Detail</h2>
              <p class="td-inbox-detail__meta">
                {{ statusLabel(selectedItem.status) }} | {{ sourceLabel(selectedItem.source) }} | created
                {{ new Date(selectedItem.createdAt).toLocaleString() }}
              </p>
            </div>
            <button class="td-btn td-btn--ghost" @click="closeDetail">Close (Esc)</button>
          </header>

          <div class="td-inbox-detail__content">
            <div v-if="captureStore.loadingDetail" class="td-placeholder td-placeholder--detail-loading">
              Loading detail...
            </div>
            <pre v-else class="td-inbox-detail__text">{{ selectedItem.rawText }}</pre>
          </div>

          <div v-if="selectedItem.provenance?.proposalId" class="td-inbox-detail__proposal-link">
            <span>A proposed board update is ready for approval.</span>
            <button
              class="td-btn td-btn--primary td-btn--sm"
              @click="openProposal(selectedItem.provenance.proposalId)"
            >
              Open in Review
            </button>
          </div>

          <footer class="td-inbox-detail__actions">
            <button
              class="td-btn td-btn--secondary"
              @click="refreshSelectedDetail"
              :disabled="captureStore.loadingDetail"
            >
              {{ captureStore.loadingDetail ? 'Refreshing...' : 'Refresh Detail' }}
            </button>
            <button
              class="td-btn td-btn--primary"
              @click="triageSelected"
              :disabled="captureStore.actionBusyItemId === selectedItem.id || !canTriageSelection(selectedItem.status)"
            >
              {{ captureStore.actionBusyItemId === selectedItem.id ? 'Working...' : triageButtonLabel(selectedItem.status) }}
            </button>
            <button
              class="td-btn td-btn--danger"
              @click="ignoreSelected"
              :disabled="captureStore.actionBusyItemId === selectedItem.id || !canMutateSelection(selectedItem.status)"
            >
              {{ captureStore.actionBusyItemId === selectedItem.id ? 'Working...' : 'Ignore' }}
            </button>
            <button
              class="td-btn td-btn--secondary"
              @click="cancelSelected"
              :disabled="captureStore.actionBusyItemId === selectedItem.id || !canMutateSelection(selectedItem.status)"
            >
              {{ captureStore.actionBusyItemId === selectedItem.id ? 'Working...' : 'Cancel' }}
            </button>
          </footer>
        </article>
      </section>
    </div>
  </div>
</template>

<style scoped>
.td-inbox {
  max-width: 1200px;
}

.td-inbox__header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: var(--td-space-4);
  margin-bottom: var(--td-space-4);
}

.td-inbox__subtitle {
  margin-top: var(--td-space-1);
  color: var(--td-text-secondary);
}

.td-inbox__board-context {
  margin-top: var(--td-space-2);
  color: var(--td-color-primary);
  font-size: var(--td-font-sm);
  font-weight: 600;
}

.td-inbox__layout {
  display: grid;
  grid-template-columns: minmax(320px, 1fr) minmax(420px, 1.4fr);
  gap: var(--td-space-4);
}

.td-inbox__list-panel,
.td-inbox__detail-panel {
  background: var(--td-surface-primary);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-lg);
  min-height: 580px;
}

.td-inbox__list-panel {
  display: flex;
  flex-direction: column;
}

.td-inbox__list-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: var(--td-space-4);
  border-bottom: 1px solid var(--td-border-default);
}

.td-inbox__count {
  font-size: var(--td-font-sm);
  color: var(--td-text-secondary);
}

.td-inbox__list {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
  padding: var(--td-space-3);
  overflow-y: auto;
  outline: none;
}

.td-inbox__list:focus-visible {
  box-shadow: inset 0 0 0 2px var(--td-border-focus);
}

.td-inbox-row {
  text-align: left;
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  background: var(--td-surface-primary);
  padding: var(--td-space-3);
  cursor: pointer;
}

.td-inbox-row--active {
  border-color: var(--td-border-focus);
}

.td-inbox-row--selected {
  background: var(--td-surface-tertiary);
}

.td-inbox-row__head {
  display: flex;
  gap: var(--td-space-2);
  margin-bottom: var(--td-space-2);
}

.td-status-chip,
.td-meta-chip {
  font-size: var(--td-font-xs);
  border-radius: var(--td-radius-sm);
  padding: 2px 8px;
  border: 1px solid var(--td-border-default);
  color: var(--td-text-secondary);
}

.td-inbox-row__excerpt {
  color: var(--td-text-primary);
  margin-bottom: var(--td-space-2);
}

.td-inbox-row__meta {
  color: var(--td-text-tertiary);
  font-size: var(--td-font-xs);
}

.td-inbox__detail-panel {
  padding: var(--td-space-4);
}

.td-inbox-detail {
  display: flex;
  flex-direction: column;
  height: 100%;
  gap: var(--td-space-4);
}

.td-inbox-detail__header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: var(--td-space-3);
}

.td-inbox-detail__meta {
  color: var(--td-text-secondary);
  font-size: var(--td-font-sm);
  margin-top: var(--td-space-1);
}

.td-inbox-detail__content {
  flex: 1;
}

.td-inbox-detail__text {
  white-space: pre-wrap;
  word-break: break-word;
  background: var(--td-surface-secondary);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  padding: var(--td-space-3);
  min-height: 320px;
  margin: 0;
  font-size: var(--td-font-sm);
  line-height: 1.45;
}

.td-inbox-detail__actions {
  display: flex;
  gap: var(--td-space-2);
  justify-content: flex-end;
}

.td-inbox-detail__proposal-link {
  display: inline-flex;
  align-items: center;
  gap: var(--td-space-2);
  font-size: var(--td-font-sm);
  color: var(--td-text-secondary);
}

.td-placeholder {
  color: var(--td-text-secondary);
  padding: var(--td-space-6);
  text-align: center;
}

.td-placeholder--empty-state {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
  align-items: center;
  justify-content: center;
}

.td-placeholder--empty-state h3 {
  margin: 0;
  color: var(--td-text-primary);
}

.td-placeholder--empty-state p {
  margin: 0;
  max-width: 320px;
  line-height: 1.6;
}

.td-placeholder__actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-2);
}

.td-placeholder--detail {
  padding-top: calc(var(--td-space-8) * 2);
}

.td-alert {
  border-radius: var(--td-radius-md);
  padding: var(--td-space-3);
}

.td-alert--error {
  background: var(--td-color-error-light);
  color: var(--td-color-error);
}

.td-btn {
  padding: var(--td-space-2) var(--td-space-3);
  border-radius: var(--td-radius-md);
  border: 1px solid transparent;
  cursor: pointer;
  text-decoration: none;
}

.td-btn--sm {
  padding: var(--td-space-1) var(--td-space-3);
  font-size: var(--td-font-xs);
}

.td-btn--primary {
  background: var(--td-color-primary);
  color: var(--td-text-inverse);
}

.td-btn--secondary {
  background: var(--td-surface-tertiary);
  color: var(--td-text-primary);
  border-color: var(--td-border-default);
}

.td-btn--ghost {
  background: transparent;
  border-color: var(--td-border-default);
  color: var(--td-text-secondary);
}

.td-btn--danger {
  background: var(--td-color-error);
  color: var(--td-text-inverse);
}

.td-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

@media (max-width: 1024px) {
  .td-inbox__layout {
    grid-template-columns: 1fr;
  }

  .td-inbox__list-panel,
  .td-inbox__detail-panel {
    min-height: 320px;
  }
}
</style>
