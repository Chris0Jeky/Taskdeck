<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'
import { useInboxOrchestrator } from '../../composables/useInboxOrchestrator'
import PaperCaptureNib from './inbox/PaperCaptureNib.vue'
import PaperCaptureComposer from './inbox/PaperCaptureComposer.vue'
import PaperTriageTable from './inbox/PaperTriageTable.vue'
import PaperHLBtn from '../../components/paper/PaperHLBtn.vue'

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
 */
type Variant = 'nib' | 'composer'

const variant = ref<Variant>('composer')
const composerRef = ref<InstanceType<typeof PaperCaptureComposer> | null>(null)
const nibBleeding = ref(false)
let bleedTimer: ReturnType<typeof setTimeout> | null = null

const {
  captureStore,
  items,
  selectedItemId,
  loadInbox,
  triageSelected,
  ignoreSelected,
} = useInboxOrchestrator({
  // Triage table doesn't use a virtual list; selecting a row is a one-shot
  // open action, so we don't need scrollToIndex.
  scrollToIndex: () => undefined,
})

function toggleVariant() {
  variant.value = variant.value === 'nib' ? 'composer' : 'nib'
}

function setVariant(next: Variant) {
  variant.value = next
}

function handleGlobalKeydown(event: KeyboardEvent) {
  // ⌘;  or Ctrl+;  toggles between the two capture variants.
  if ((event.metaKey || event.ctrlKey) && event.key === ';') {
    event.preventDefault()
    toggleVariant()
  }
}

async function dispatchCapture(text: string, opts: { boardId?: string | null } = {}): Promise<boolean> {
  try {
    await captureStore.createItem({
      boardId: opts.boardId ?? null,
      text,
      source: 'Typed',
    })
    await loadInbox()
    return true
  } catch {
    // captureStore handles toast surfacing; we keep the surface usable.
    return false
  }
}

async function onNibSubmit(text: string) {
  // Show the static ember placeholder (TODO: ink bleed) for ~1.4s, regardless
  // of whether the create call resolves first; the placeholder is purely
  // motion stand-in.
  nibBleeding.value = true
  if (bleedTimer) {
    clearTimeout(bleedTimer)
  }
  bleedTimer = setTimeout(() => {
    nibBleeding.value = false
    bleedTimer = null
  }, 1400)

  await dispatchCapture(text)
}

async function onComposerSubmit(payload: {
  text: string
  boardId: string | null
  labels: string[]
  dueAt: string | null
}) {
  // Labels / dueAt aren't part of CreateCaptureItemDto yet — they're surfaced
  // for the design but not persisted by the current API.  We still pass the
  // boardId so the capture lands on the right board.
  const created = await dispatchCapture(payload.text, { boardId: payload.boardId })
  if (created) {
    composerRef.value?.resetDraft()
  }
}

function onComposerAttachments(_files: File[]) {
  // Real upload pipeline is out of scope for PAPER-07.  Bubble silently for
  // now; tests assert the event fires.
}

async function onTriageAccept(itemId: string) {
  selectedItemId.value = itemId
  await triageSelected()
}

async function onTriageReject(itemId: string) {
  selectedItemId.value = itemId
  await ignoreSelected()
}

function onTriageOpen(itemId: string) {
  selectedItemId.value = itemId
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
})

defineExpose({ variant, toggleVariant, setVariant })
</script>

<template>
  <div class="paper-inbox" :data-variant="variant">
    <header class="paper-inbox__header">
      <div>
        <div class="tk-eyebrow">Inbox · capture surface · {{ items.length }} in queue</div>
        <h1 class="tk-h1 paper-inbox__title">
          What's on your mind, <em>quickly?</em>
        </h1>
        <p class="tk-lede paper-inbox__lede">
          Drop the thought. It will sit here, untouched, until you triage it.
          Nothing flows to the board without your approval.
        </p>
      </div>

      <div class="paper-inbox__variant-toggle" role="tablist" aria-label="Capture variant">
        <PaperHLBtn
          role="tab"
          :aria-selected="variant === 'nib'"
          :variant="variant === 'nib' ? 'ember' : 'default'"
          label="Nib"
          @click="setVariant('nib')"
        />
        <PaperHLBtn
          role="tab"
          :aria-selected="variant === 'composer'"
          :variant="variant === 'composer' ? 'ember' : 'default'"
          label="Composer"
          kbd="⌘;"
          @click="setVariant('composer')"
        />
      </div>
    </header>

    <section class="paper-inbox__capture" data-testid="paper-inbox-capture">
      <PaperCaptureNib
        v-if="variant === 'nib'"
        :bleeding="nibBleeding"
        @submit="onNibSubmit"
      />
      <PaperCaptureComposer
        v-else
        ref="composerRef"
        @submit="onComposerSubmit"
        @attachments-changed="onComposerAttachments"
      />
    </section>

    <PaperTriageTable
      :items="items"
      :loading-list="captureStore.loadingList"
      :list-error="captureStore.listError"
      :action-busy-item-id="captureStore.actionBusyItemId"
      @accept="onTriageAccept"
      @reject="onTriageReject"
      @open="onTriageOpen"
      @retry="loadInbox"
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
@media (max-width: 720px) {
  .paper-inbox__header {
    flex-direction: column;
  }
}
</style>
