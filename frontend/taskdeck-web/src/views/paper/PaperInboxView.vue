<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { getErrorDisplay } from '../../composables/useErrorMapper'
import { useInboxCounts } from '../../composables/useInboxCounts'
import { useInboxOrchestrator } from '../../composables/useInboxOrchestrator'
import { isTriageTerminalStatus } from '../../types/capture'
import PaperCaptureNib from './inbox/PaperCaptureNib.vue'
import PaperCaptureComposer from './inbox/PaperCaptureComposer.vue'
import PaperTriageTable from './inbox/PaperTriageTable.vue'
import PaperHLBtn from '../../components/paper/PaperHLBtn.vue'
import PaperScopeDisclosure from '../../components/paper/PaperScopeDisclosure.vue'

/**
 * PaperInboxView — Paper-themed Inbox / Capture orchestrator.
 *
 * The caller (`InboxView.vue`) is responsible for `paperThemeStore.isOn`;
 * this view assumes the Paper class is already on `<body>`.
 *
 * Two capture variants:
 *   A · Single-line nib (focus-mode italic input)
 *   B · Composer ledger (multi-line + metadata sidebar)
 *
 * `⌘;` (or `Ctrl+;` on non-Mac) toggles between them globally.  Default is
 * the composer ledger, which is the variant the design handoff calls "the
 * structured/recommended path".
 *
 * Copy lives in the `inbox.*` catalogs (ADR-0054). "Nib" and "Composer" are
 * Taskdeck's own names for the two variants and stay in English in every
 * locale — they are keyed only so a translator sees them in context and can
 * see they are deliberately untranslated.
 */
type Variant = 'nib' | 'composer'

const variant = ref<Variant>('composer')
const { t } = useI18n()
const composerRef = ref<InstanceType<typeof PaperCaptureComposer> | null>(null)
const nibRef = ref<InstanceType<typeof PaperCaptureNib> | null>(null)
const nibBleeding = ref(false)
const captureSubmitting = ref(false)
const captureError = ref<string | null>(null)
const captureMetadataCompatibilityWarning = ref(false)
let bleedTimer: ReturnType<typeof setTimeout> | null = null

const {
  captureStore,
  items,
  activeBoardId,
  activeColumnId,
  activeBoardName,
  activeColumnName,
  loadInbox,
  clearScope,
} = useInboxOrchestrator({
  scrollToIndex: () => undefined,
})

// Header counters (GH-1974). `pendingTriageCount` is the sidebar badge's own
// definition applied to these rows; `capturedCount` is everything fetched.
// The eyebrow labels them separately — the total is not a queue.
const { pendingTriageCount, capturedCount } = useInboxCounts(items)

const scopeLabel = computed(() => {
  if (!activeBoardId.value) return ''
  return activeColumnId.value
    ? t('inbox.scope.boardAndColumn', { board: activeBoardName.value, column: activeColumnName.value })
    : t('inbox.scope.board', { board: activeBoardName.value })
})

let stopTriagePolling: (() => void) | null = null

function toggleVariant() {
  setVariant(variant.value === 'nib' ? 'composer' : 'nib')
}

function setVariant(next: Variant) {
  variant.value = next
  void nextTick(() => {
    if (next === 'nib') {
      nibRef.value?.focus()
      return
    }
    composerRef.value?.focus()
  })
}

function handleGlobalKeydown(event: KeyboardEvent) {
  // ⌘;  or Ctrl+;  toggles between the two capture variants.
  if ((event.metaKey || event.ctrlKey) && event.key === ';') {
    event.preventDefault()
    toggleVariant()
  }
}

async function dispatchCapture(
  text: string,
  opts: { boardId?: string | null; dueDate?: string | null; labels?: string[] } = {},
): Promise<boolean> {
  if (captureSubmitting.value) {
    return false
  }

  captureError.value = null
  captureMetadataCompatibilityWarning.value = false
  captureSubmitting.value = true
  try {
    const metadataRequested = Object.hasOwn(opts, 'dueDate') || Object.hasOwn(opts, 'labels')
    const created = await captureStore.createItem({
      boardId: Object.hasOwn(opts, 'boardId') ? opts.boardId ?? null : activeBoardId.value,
      text,
      source: 'Typed',
      ...(metadataRequested
        ? { dueDate: opts.dueDate ?? null, labels: opts.labels ?? [] }
        : {}),
    })
    if (metadataRequested && !Object.hasOwn(created, 'metadata')) {
      // Split web/API deployments can briefly pair this SPA with an older API.
      // The text is already saved, so acknowledge it and warn against a retry
      // that would create a duplicate capture.
      captureMetadataCompatibilityWarning.value = true
    }
    await loadInbox().catch(() => {
      // The capture already exists. The orchestrator/store owns listError and
      // its toast; treating this refresh failure as a rejected create would
      // retain the draft and invite a duplicate capture on retry.
    })
    return true
  } catch (error: unknown) {
    // The store's toast remains useful global feedback, but it expires. Keep
    // an inspectable receipt beside the draft until the user retries (GH-1938).
    captureError.value = getErrorDisplay(error, t('inbox.capture.errorFallback')).message
    return false
  } finally {
    captureSubmitting.value = false
  }
}

async function onNibSubmit(text: string) {
  const created = await dispatchCapture(text)
  if (!created) {
    return
  }

  nibRef.value?.resetDraft()
  // Show the static ember placeholder (TODO: ink bleed) for ~1.4s after a
  // confirmed create; the placeholder is purely motion stand-in.
  nibBleeding.value = true
  if (bleedTimer) {
    clearTimeout(bleedTimer)
  }
  bleedTimer = setTimeout(() => {
    nibBleeding.value = false
    bleedTimer = null
    void nextTick(() => {
      if (variant.value === 'nib') {
        nibRef.value?.focus()
      }
    })
  }, 1400)
}

async function onComposerSubmit(payload: {
  text: string
  boardId: string | null
  labels: string[]
  dueAt: string | null
}) {
  const metadata = payload.dueAt || payload.labels.length > 0
    ? { dueDate: payload.dueAt, labels: payload.labels }
    : {}
  const created = await dispatchCapture(payload.text, {
    boardId: payload.boardId,
    ...metadata,
  })
  if (created) {
    composerRef.value?.resetDraft()
  }
}

async function onTriageAccept(itemId: string, boardId?: string | null) {
  if (stopTriagePolling) {
    stopTriagePolling()
    stopTriagePolling = null
  }
  try {
    await captureStore.triageItem(itemId, boardId)
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
  }
}

async function onTriageReject(itemId: string) {
  try {
    await captureStore.ignoreItem(itemId)
  } catch {
    // Store handles toast + error state.
  }
}

function onTriageOpen(_itemId: string) {
  // Paper Inbox has no detail panel. Avoid mutating selectedItemId here because
  // the legacy selection watcher owns triage polling lifecycle cleanup.
}

onMounted(() => {
  window.addEventListener('keydown', handleGlobalKeydown)
})

onUnmounted(() => {
  window.removeEventListener('keydown', handleGlobalKeydown)
  if (bleedTimer) {
    clearTimeout(bleedTimer)
    bleedTimer = null
  }
  if (stopTriagePolling) {
    stopTriagePolling()
    stopTriagePolling = null
  }
})

defineExpose({ variant, toggleVariant, setVariant })
</script>

<template>
  <div class="paper-inbox" :data-variant="variant">
    <header class="paper-inbox__header">
      <div>
        <!-- The third argument is the plural CHOICE: it/es agree the participle
             with the total ("1 catturato" vs "2 catturati"), so the count has to
             reach the catalog as a choice and not only as an interpolation. -->
        <div class="tk-eyebrow" data-testid="paper-inbox-eyebrow">
          {{
            $t(
              'inbox.eyebrow',
              { pending: pendingTriageCount, total: capturedCount },
              capturedCount,
            )
          }}
        </div>
        <h1 class="tk-h1 paper-inbox__title">
          {{ $t('inbox.title.lead') }} <em>{{ $t('inbox.title.emphasis') }}</em>
        </h1>
        <p class="tk-lede paper-inbox__lede">
          {{ $t('inbox.lede') }}
        </p>
        <PaperScopeDisclosure
          v-if="scopeLabel"
          :label="scopeLabel"
          :clear-label="$t('inbox.scope.clear')"
          @clear="clearScope"
        />
      </div>

      <div
        class="paper-inbox__variant-toggle"
        role="tablist"
        :aria-label="$t('inbox.variantToggle.label')"
      >
        <PaperHLBtn
          role="tab"
          :aria-selected="variant === 'nib'"
          :variant="variant === 'nib' ? 'ember' : 'default'"
          :label="$t('inbox.variant.nib')"
          @click="setVariant('nib')"
        />
        <PaperHLBtn
          role="tab"
          :aria-selected="variant === 'composer'"
          :variant="variant === 'composer' ? 'ember' : 'default'"
          :label="$t('inbox.variant.composer')"
          kbd="⌘;"
          @click="setVariant('composer')"
        />
      </div>
    </header>

    <section class="paper-inbox__capture" data-testid="paper-inbox-capture">
      <PaperCaptureNib
        v-show="variant === 'nib'"
        ref="nibRef"
        :bleeding="nibBleeding"
        :submitting="captureSubmitting"
        @submit="onNibSubmit"
      />
      <PaperCaptureComposer
        v-show="variant === 'composer'"
        ref="composerRef"
        :default-board-id="activeBoardId"
        :submitting="captureSubmitting"
        @submit="onComposerSubmit"
      />
      <p
        v-if="captureError"
        class="paper-inbox__capture-error"
        role="alert"
        data-testid="paper-inbox-capture-error"
      >
        <strong>{{ $t('inbox.capture.errorLead') }}</strong>
        <span>{{ $t('inbox.capture.errorDetail', { reason: captureError }) }}</span>
      </p>
      <p
        v-if="captureMetadataCompatibilityWarning"
        class="paper-inbox__capture-compatibility-warning"
        role="status"
        data-testid="paper-inbox-capture-metadata-compatibility-warning"
      >
        <strong>{{ $t('inbox.capture.metadataCompatibilityLead') }}</strong>
        <span>{{ $t('inbox.capture.metadataCompatibilityDetail') }}</span>
      </p>
    </section>

    <PaperTriageTable
      :items="items"
      :loading-list="captureStore.loadingList"
      :list-error="captureStore.listError"
      :action-busy-item-id="captureStore.actionBusyItemId"
      :triage-polling-item-id="captureStore.triagePollingItemId"
      :scope-label="scopeLabel"
      :scope-clear-label="$t('inbox.scope.clear')"
      @accept="onTriageAccept"
      @reject="onTriageReject"
      @open="onTriageOpen"
      @retry="loadInbox"
      @clear-scope="clearScope"
    />
  </div>
</template>

<style scoped>
.paper-inbox {
  max-width: 1100px;
  margin: 0 auto;
  padding: 8px 0 40px;
  font-family: var(--sans);
}
.paper-inbox__header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 24px;
  margin-bottom: 20px;
}
.paper-inbox__title {
  margin: 8px 0 6px;
  font-family: var(--serif);
  font-weight: 400;
  font-size: 36px;
  color: var(--ink-deep);
  letter-spacing: -0.01em;
}
.paper-inbox__title em {
  color: var(--ember);
  font-style: italic;
}
.paper-inbox__lede {
  margin: 0;
  color: var(--ink-2);
  font-size: 14px;
  max-width: 60ch;
}
.paper-inbox__variant-toggle {
  display: flex;
  gap: 6px;
  flex-shrink: 0;
}
.paper-inbox__capture {
  margin-top: 8px;
}
.paper-inbox__capture-error {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 8px;
  margin: 8px 0 0;
  padding: 10px 14px;
  border: 1px solid var(--ember);
  border-radius: var(--r-1);
  background: var(--ember-tint);
  color: var(--ember-ink);
  font-family: var(--mono);
  font-size: 11px;
  line-height: 1.5;
}
.paper-inbox__capture-compatibility-warning {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 8px;
  margin: 8px 0 0;
  padding: 10px 14px;
  border: 1px dashed var(--ink-2);
  border-radius: var(--r-1);
  background: var(--paper-2);
  color: var(--ink-2);
  font-family: var(--mono);
  font-size: 11px;
  line-height: 1.5;
}
@media (max-width: 720px) {
  .paper-inbox__header {
    flex-direction: column;
  }
}
</style>
