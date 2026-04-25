<script setup lang="ts">
import { computed } from 'vue'
import PaperHLBtn from '../../../components/paper/PaperHLBtn.vue'
import PaperTagstamp from '../../../components/paper/PaperTagstamp.vue'
import type { CaptureItemSummary } from '../../../types/capture'

/**
 * PaperTriageTable — captured items list rendered in the paper-card ledger
 * style.  Each row surfaces excerpt, source/status as Paper tagstamps, and
 * accept / reject actions as hairline buttons.
 *
 * Idempotent: `actionBusyItemId` mirrors the captureStore field so a
 * double-click on Accept can't fire `accept` twice for the same row.
 */
const props = defineProps<{
  items: CaptureItemSummary[]
  loadingList?: boolean
  listError?: string | null
  actionBusyItemId?: string | null
}>()

const emit = defineEmits<{
  (event: 'accept', itemId: string): void
  (event: 'reject', itemId: string): void
  (event: 'open', itemId: string): void
  (event: 'retry'): void
}>()

const hasItems = computed(() => props.items.length > 0)

function isBusy(itemId: string): boolean {
  return props.actionBusyItemId === itemId
}

function onAccept(itemId: string) {
  if (isBusy(itemId)) return
  emit('accept', itemId)
}

function onReject(itemId: string) {
  if (isBusy(itemId)) return
  emit('reject', itemId)
}

function statusTone(status: string | number): 'ember' | 'applied' | 'overdue' | 'mute' {
  const value = typeof status === 'string' ? status.toLowerCase() : String(status).toLowerCase()
  if (value.includes('failed') || value.includes('error')) return 'overdue'
  if (value.includes('triag')) return 'ember'
  if (value.includes('proposed')) return 'ember'
  if (value.includes('applied') || value.includes('accept')) return 'applied'
  return 'mute'
}

function formatTime(iso: string): string {
  try {
    const d = new Date(iso)
    if (Number.isNaN(d.getTime())) return ''
    return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
  } catch {
    return ''
  }
}
</script>

<template>
  <section class="paper-triage" aria-label="Captured items">
    <header class="paper-triage__header">
      <h2 class="tk-h3 paper-triage__title">Today's captures</h2>
      <span class="tk-meta">
        {{ hasItems ? `${items.length} item${items.length === 1 ? '' : 's'} · most recent first` : 'No captures yet' }}
      </span>
    </header>

    <div v-if="loadingList && !hasItems" class="paper-triage__empty">
      <span class="tk-meta">Loading…</span>
    </div>

    <div v-else-if="listError" class="paper-triage__empty paper-triage__empty--error" role="alert">
      <p class="tk-body">{{ listError }}</p>
      <button type="button" class="paper-triage__retry" @click="emit('retry')">
        Retry
      </button>
    </div>

    <div v-if="!loadingList && !listError && !hasItems" class="paper-triage__empty">
      <p class="tk-body">A pen and a phrase. Drop a thought above to start.</p>
    </div>

    <ul v-if="hasItems" class="paper-triage__list">
      <li
        v-for="item in items"
        :key="item.id"
        class="paper-triage__row"
        :data-item-id="item.id"
      >
        <button
          type="button"
          class="paper-triage__open"
          :aria-label="`Open capture ${item.id}`"
          @click="emit('open', item.id)"
        >
          <span class="tk-serial paper-triage__time">{{ formatTime(item.createdAt) }}</span>
          <span class="paper-triage__excerpt">{{ item.textExcerpt }}</span>
        </button>

        <div class="paper-triage__tags">
          <PaperTagstamp :tone="statusTone(item.status)">{{ item.status }}</PaperTagstamp>
          <PaperTagstamp tone="mute">{{ item.source }}</PaperTagstamp>
        </div>

        <div class="paper-triage__actions">
          <PaperHLBtn
            label="Accept"
            variant="ember"
            :disabled="isBusy(item.id)"
            data-action="accept"
            @click="onAccept(item.id)"
          />
          <PaperHLBtn
            label="Reject"
            variant="ghost"
            :disabled="isBusy(item.id)"
            data-action="reject"
            @click="onReject(item.id)"
          />
        </div>
      </li>
    </ul>
  </section>
</template>

<style scoped>
.paper-triage {
  margin-top: 32px;
}
.paper-triage__header {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  margin-bottom: 12px;
}
.paper-triage__title {
  margin: 0;
  font-family: var(--serif);
  font-size: 20px;
  color: var(--ink-deep);
}
.paper-triage__empty {
  padding: 22px;
  border: 1px solid var(--line-soft);
  border-radius: 4px;
  background: var(--paper-card);
  color: var(--mute);
}
.paper-triage__empty--error {
  border-color: var(--overdue);
  color: var(--overdue);
}
.paper-triage__retry {
  margin-top: 10px;
  border: 1px solid var(--line);
  border-radius: 2px;
  background: var(--paper);
  color: var(--ink);
  font-family: var(--mono);
  font-size: 11px;
  letter-spacing: 0.04em;
  padding: 6px 10px;
  text-transform: uppercase;
  cursor: pointer;
}
.paper-triage__list {
  margin: 0;
  padding: 0;
  list-style: none;
  border: 1px solid var(--line-soft);
  border-radius: 4px;
  background: var(--paper-card);
  overflow: hidden;
}
.paper-triage__row {
  display: grid;
  grid-template-columns: 1fr auto auto;
  gap: 16px;
  align-items: center;
  padding: 14px 18px;
  border-bottom: 1px solid var(--line-soft);
}
.paper-triage__row:last-child {
  border-bottom: 0;
}
.paper-triage__open {
  display: grid;
  grid-template-columns: 60px 1fr;
  gap: 12px;
  align-items: baseline;
  text-align: left;
  background: transparent;
  border: 0;
  padding: 0;
  cursor: pointer;
  color: var(--ink);
  min-width: 0;
}
.paper-triage__time {
  color: var(--mute);
  font-size: 11px;
}
.paper-triage__excerpt {
  font-family: var(--sans);
  font-size: 13.5px;
  line-height: 1.5;
  color: var(--ink);
  overflow: hidden;
  text-overflow: ellipsis;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
}
.paper-triage__tags {
  display: flex;
  gap: 6px;
}
.paper-triage__actions {
  display: flex;
  gap: 6px;
}
</style>
