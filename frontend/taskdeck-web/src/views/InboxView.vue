<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import WorkspaceHelpCallout from '../components/workspace/WorkspaceHelpCallout.vue'
import CaptureModal from '../components/common/CaptureModal.vue'
import { useCaptureStore } from '../store/captureStore'
import { isTriageTerminalStatus } from '../types/capture'
import type { CaptureItem, CaptureItemSummary, CaptureSourceValue, CaptureStatusValue } from '../types/capture'
import { registerEscapeHandler } from '../composables/useEscapeStack'
import { useVirtualList } from '../composables/useVirtualList'
import { usePerformanceMark } from '../composables/usePerformanceMark'
import { normalizeBoardIdQueryParam } from '../utils/navigation'

const captureStore = useCaptureStore()
const router = useRouter()
const route = useRoute()
const inboxLoadPerf = usePerformanceMark('inbox-load')
const selectedItemId = ref<string | null>(null)
const hashLoadFailedItemId = ref<string | null>(null)
const activeItemIndex = ref(0)
const showCaptureModal = ref(false)
let stopTriagePolling: (() => void) | null = null

function openCaptureModal() {
  showCaptureModal.value = true
}

function closeCaptureModal() {
  showCaptureModal.value = false
}

async function handleCaptureCreated() {
  closeCaptureModal()
  await loadInbox()
}

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

const {
  parentRef: virtualParentRef,
  virtualItemEls,
  virtualRows,
  totalSize: virtualTotalSize,
  translateY: virtualTranslateY,
  scrollToIndex: virtualScrollToIndex,
} = useVirtualList({
  count: computed(() => items.value.length),
  estimateSize: 80,
  overscan: 5,
})

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
  inboxLoadPerf.start()
  try {
    await captureStore.fetchItems({
      limit: 200,
      ...(activeBoardId.value ? { boardId: activeBoardId.value } : {}),
    })
  } catch {
    // Store handles toast + error state.
  }

  await openItemFromHash()
  inboxLoadPerf.end()
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
  virtualScrollToIndex(activeItemIndex.value)
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
  const itemId = selectedItemId.value
  if (!itemId) {
    return
  }

  if (stopTriagePolling) {
    stopTriagePolling()
    stopTriagePolling = null
  }

  try {
    await captureStore.triageItem(itemId)

    const latestStatus = captureStore.detailById[itemId]?.status
    if (latestStatus !== undefined && isTriageTerminalStatus(latestStatus)) {
      return
    }

    stopTriagePolling = captureStore.pollTriageCompletion(itemId)
  } catch {
    if (stopTriagePolling) {
      stopTriagePolling()
      stopTriagePolling = null
    }
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
    return captureStore.triagePollingItemId === selectedItemId.value
      ? 'Triaging (checking...)'
      : 'Triaging...'
  }

  if (label === 'Ready for review' || label === 'Triaged') {
    return 'Triage Complete'
  }

  if (label === 'Applied to board') {
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
  if (stopTriagePolling) {
    stopTriagePolling()
    stopTriagePolling = null
  }

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

onUnmounted(() => {
  if (stopTriagePolling) {
    stopTriagePolling()
    stopTriagePolling = null
  }
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
      <div class="td-inbox__header-actions">
        <button
          class="td-btn td-btn--primary"
          aria-label="Open capture modal to add a new inbox item"
          @click="openCaptureModal"
        >
          + New Capture
        </button>
        <button class="td-btn td-btn--secondary" @click="loadInbox" :disabled="captureStore.loadingList">
          {{ captureStore.loadingList ? 'Refreshing...' : 'Refresh' }}
        </button>
      </div>
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
          ref="virtualParentRef"
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
            <p>Capture a note or transcript to get started. Once triage runs, proposals will appear in Review.</p>
            <div class="td-placeholder__actions">
              <button
                class="td-btn td-btn--primary td-btn--sm"
                aria-label="Open capture modal to add a new inbox item"
                @click="openCaptureModal"
              >
                + New Capture
              </button>
              <button class="td-btn td-btn--secondary td-btn--sm" @click="openRoute('/workspace/home')">Open Home</button>
              <button class="td-btn td-btn--secondary td-btn--sm" @click="openRoute('/workspace/today')">Open Today</button>
              <button class="td-btn td-btn--secondary td-btn--sm" @click="openReview">Open Review</button>
            </div>
          </div>

          <div
            v-if="captureStore.hasItems && !captureStore.loadingList && !captureStore.listError"
            :style="{ height: `${virtualTotalSize}px`, width: '100%', position: 'relative' }"
          >
            <div
              :style="{
                position: 'absolute',
                top: 0,
                left: 0,
                width: '100%',
                transform: `translateY(${virtualTranslateY}px)`,
              }"
            >
              <div
                v-for="virtualRow in virtualRows"
                :key="String(virtualRow.key)"
                :data-index="virtualRow.index"
                ref="virtualItemEls"
              >
                <template v-if="items[virtualRow.index]">
                  <div
                    :id="`td-inbox-option-${virtualRow.index}`"
                    :data-inbox-index="virtualRow.index"
                    data-testid="inbox-item"
                    :data-capture-id="items[virtualRow.index]!.id"
                    :class="[
                      'td-inbox-row',
                      virtualRow.index === activeItemIndex ? 'td-inbox-row--active' : '',
                      selectedItemId === items[virtualRow.index]!.id ? 'td-inbox-row--selected' : ''
                    ]"
                    role="option"
                    :aria-selected="selectedItemId === items[virtualRow.index]!.id"
                    @mouseenter="setActiveIndex(virtualRow.index)"
                    @click="openItemFromList(items[virtualRow.index]!, virtualRow.index)"
                  >
                    <div class="td-inbox-row__head">
                      <span class="td-status-chip">{{ statusLabel(items[virtualRow.index]!.status) }}</span>
                      <span class="td-meta-chip">{{ sourceLabel(items[virtualRow.index]!.source) }}</span>
                    </div>
                    <p class="td-inbox-row__excerpt">{{ items[virtualRow.index]!.textExcerpt }}</p>
                    <p class="td-inbox-row__meta">{{ new Date(items[virtualRow.index]!.createdAt).toLocaleString() }}</p>
                  </div>
                </template>
              </div>
            </div>
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

  <Teleport to="body">
    <CaptureModal
      v-if="showCaptureModal"
      @close="closeCaptureModal"
      @created="handleCaptureCreated"
    />
  </Teleport>
</template>

<style scoped>
/* ─── Obsidian & Ember — InboxView ─── */

.td-inbox {
  max-width: 1200px;
}

/* ─── Page header ─── */

.td-inbox__header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: var(--td-space-4);
  margin-bottom: var(--td-space-4);
}

.td-inbox__header-actions {
  display: flex;
  align-items: center;
  gap: var(--td-space-2);
  flex-shrink: 0;
}

.td-inbox__subtitle {
  margin-top: var(--td-space-1);
  color: var(--td-text-secondary);
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: 10px;
  text-transform: uppercase;
  letter-spacing: 0.2em;
}

.td-inbox__board-context {
  margin-top: var(--td-space-2);
  color: var(--td-color-ember);
  font-size: var(--td-font-sm);
  font-weight: 600;
}

/* ─── Two-column layout ─── */

.td-inbox__layout {
  display: grid;
  grid-template-columns: minmax(320px, 1fr) minmax(420px, 1.4fr);
  gap: var(--td-space-4);
}

.td-inbox__list-panel,
.td-inbox__detail-panel {
  border: 0.5px solid var(--td-border-ghost);
  border-radius: var(--td-radius-lg);
  min-height: 580px;
}

.td-inbox__list-panel {
  display: flex;
  flex-direction: column;
  background: var(--td-surface-container, #201f1f);
}

/* ─── List header ─── */

.td-inbox__list-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: var(--td-space-4);
  border-bottom: 0.5px solid var(--td-border-default);
}

.td-inbox__list-header h2 {
  font-family: 'Manrope', system-ui, sans-serif;
  color: var(--td-text-primary);
}

.td-inbox__count {
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: 10px;
  text-transform: uppercase;
  letter-spacing: 0.2em;
  color: var(--td-text-tertiary);
  background: var(--td-surface-container-highest);
  padding: 2px 10px;
  border-radius: var(--td-radius-sm);
}

/* ─── Scrollable list ─── */

.td-inbox__list {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
  padding: var(--td-space-3);
  overflow-y: auto;
  outline: none;
}

.td-inbox__list:focus-visible {
  box-shadow: inset 0 0 0 2px rgba(255, 77, 77, 0.35);
}

/* ─── List rows ─── */

.td-inbox-row {
  text-align: left;
  border: 0.5px solid var(--td-border-ghost);
  border-left: 2px solid transparent;
  border-radius: var(--td-radius-md);
  background: var(--td-surface-container, #201f1f);
  padding: var(--td-space-3);
  cursor: pointer;
  transition: background var(--td-transition-fast, 120ms) ease,
              border-color var(--td-transition-fast, 120ms) ease;
}

.td-inbox-row--active {
  background: var(--td-surface-bright, #3a3939);
  border-left-color: var(--td-color-ember, #ff4d4d);
}

.td-inbox-row--selected {
  background: var(--td-surface-high, #2a2a2a);
  border-left-color: var(--td-color-ember, #ff4d4d);
  box-shadow: var(--td-shadow-sm, 0 1px 3px rgba(0, 0, 0, 0.4));
}

.td-inbox-row__head {
  display: flex;
  gap: var(--td-space-2);
  margin-bottom: var(--td-space-2);
}

/* ─── Status / meta chips ─── */

.td-status-chip,
.td-meta-chip {
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: 9px;
  text-transform: uppercase;
  letter-spacing: 0.2em;
  border-radius: var(--td-radius-sm);
  padding: 2px 8px;
  border: 0.5px solid var(--td-border-default);
  background: var(--td-surface-container-highest);
  color: var(--td-text-secondary);
}

.td-inbox-row__excerpt {
  color: var(--td-text-primary);
  margin-bottom: var(--td-space-2);
  font-size: var(--td-font-sm);
  line-height: 1.5;
}

.td-inbox-row__meta {
  color: var(--td-text-tertiary);
  font-size: var(--td-font-xs);
}

/* ─── Detail panel ─── */

.td-inbox__detail-panel {
  padding: var(--td-space-4);
  background: var(--td-surface-low, #1c1b1b);
}

.td-inbox-detail {
  display: flex;
  flex-direction: column;
  height: 100%;
  gap: var(--td-space-4);
}

/* Glass header effect */
.td-inbox-detail__header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: var(--td-space-3);
  background: var(--td-glass-bg, rgba(32, 31, 31, 0.8));
  backdrop-filter: blur(var(--td-glass-blur, 16px));
  -webkit-backdrop-filter: blur(var(--td-glass-blur, 16px));
  border: 0.5px solid var(--td-border-ghost);
  border-radius: var(--td-radius-md);
  padding: var(--td-space-3) var(--td-space-4);
}

.td-inbox-detail__header h2 {
  font-family: 'Manrope', system-ui, sans-serif;
  color: var(--td-text-primary);
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
  background: var(--td-surface-lowest, #0e0e0e);
  border: 0.5px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  padding: var(--td-space-3);
  min-height: 320px;
  margin: 0;
  font-size: var(--td-font-sm);
  line-height: 1.45;
  color: var(--td-text-primary);
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
  background: var(--td-surface-container, #201f1f);
  border: 0.5px solid var(--td-border-ghost);
  border-radius: var(--td-radius-md);
  padding: var(--td-space-2) var(--td-space-3);
}

/* ─── Placeholder / empty states ─── */

.td-placeholder {
  color: var(--td-text-tertiary);
  padding: var(--td-space-6);
  text-align: center;
}

.td-placeholder--empty-state {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
  align-items: center;
  justify-content: center;
  border: 0.5px solid var(--td-border-ghost);
  border-radius: var(--td-radius-md);
  margin: var(--td-space-4);
  padding: var(--td-space-6);
}

.td-placeholder--empty-state h3 {
  margin: 0;
  color: var(--td-text-primary);
  font-family: 'Manrope', system-ui, sans-serif;
}

.td-placeholder--empty-state p {
  margin: 0;
  max-width: 320px;
  line-height: 1.6;
  color: var(--td-text-tertiary);
}

.td-placeholder__actions {
  display: flex;
  flex-wrap: wrap;
  gap: var(--td-space-2);
  margin-top: var(--td-space-2);
}

.td-placeholder--detail {
  padding-top: calc(var(--td-space-8) * 2);
  color: var(--td-text-tertiary);
}

.td-placeholder--detail-loading {
  color: var(--td-text-tertiary);
  padding: var(--td-space-6);
  text-align: center;
}

/* ─── Alerts ─── */

.td-alert {
  border-radius: var(--td-radius-md);
  padding: var(--td-space-3);
}

.td-alert--error {
  background: rgba(255, 77, 77, 0.08);
  color: var(--td-color-ember, #ff4d4d);
  border: 0.5px solid rgba(255, 77, 77, 0.2);
}

/* .td-btn variants are global (src/style.css) */

/* ─── Responsive ─── */

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
