<script setup lang="ts">
import { computed } from 'vue'
import { useAuditStore } from '../../store/auditStore'
import { useVirtualList } from '../../composables/useVirtualList'
import { formatAction, formatTimestamp } from '../../composables/useActivityQuery'

defineProps<{
  emptyStateTitle: string
  emptyStateBody: string
}>()

const emit = defineEmits<{
  'navigate': [path: string]
}>()

const audit = useAuditStore()

const {
  parentRef: timelineParentRef,
  virtualItemEls: timelineItemEls,
  virtualRows: timelineVirtualRows,
  totalSize: timelineTotalSize,
  translateY: timelineTranslateY,
} = useVirtualList({
  count: computed(() => audit.entries.length),
  estimateSize: 100,
  overscan: 5,
})
</script>

<template>
  <div v-if="audit.loading" class="td-loading">Loading activity...</div>

  <div v-else-if="audit.entries.length === 0" class="td-timeline">
    <div class="td-empty td-empty--panel">
      <h2 class="td-empty__title">{{ emptyStateTitle }}</h2>
      <p class="td-empty__body">{{ emptyStateBody }}</p>
      <div class="td-empty__actions">
        <button class="td-btn td-btn--primary td-btn--sm" @click="emit('navigate', '/workspace/review')">Open Review</button>
        <button class="td-btn td-btn--ghost td-btn--sm" @click="emit('navigate', '/workspace/boards')">Open Boards</button>
      </div>
    </div>
  </div>

  <div
    v-else
    ref="timelineParentRef"
    class="td-timeline td-timeline--virtual"
  >
    <div
      :style="{ height: `${timelineTotalSize}px`, width: '100%', position: 'relative' }"
    >
      <div
        :style="{
          position: 'absolute',
          top: 0,
          left: 0,
          width: '100%',
          transform: `translateY(${timelineTranslateY}px)`,
        }"
      >
        <div
          v-for="virtualRow in timelineVirtualRows"
          :key="String(virtualRow.key)"
          :data-index="virtualRow.index"
          ref="timelineItemEls"
        >
          <template v-if="audit.entries[virtualRow.index]">
            <div class="td-timeline__entry">
              <div class="td-timeline__dot"></div>
              <div class="td-timeline__content">
                <div class="td-timeline__header">
                  <span class="td-timeline__action">{{ formatAction(audit.entries[virtualRow.index]!.action) }}</span>
                  <span class="td-timeline__time">{{ formatTimestamp(audit.entries[virtualRow.index]!.timestamp) }}</span>
                </div>
                <div class="td-timeline__details">
                  <span class="td-timeline__entity">{{ audit.entries[virtualRow.index]!.entityType }} - {{ audit.entries[virtualRow.index]!.entityId }}</span>
                  <span v-if="audit.entries[virtualRow.index]!.userName" class="td-timeline__actor">by {{ audit.entries[virtualRow.index]!.userName }}</span>
                </div>
                <div v-if="audit.entries[virtualRow.index]!.changes" class="td-timeline__message">{{ audit.entries[virtualRow.index]!.changes }}</div>
              </div>
            </div>
          </template>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.td-loading { text-align: center; padding: var(--td-space-8); color: var(--td-text-secondary); }
.td-empty { text-align: center; padding: var(--td-space-8); color: var(--td-text-tertiary); }
.td-empty--panel { display: flex; flex-direction: column; gap: var(--td-space-2); align-items: center; justify-content: center; border: 1px dashed var(--td-border-default); border-radius: var(--td-radius-lg); background: var(--td-surface-primary); }
.td-empty__title { margin: 0; color: var(--td-text-primary); font-size: var(--td-font-lg); }
.td-empty__body { margin: 0; max-width: 500px; line-height: 1.6; }
.td-empty__actions { display: flex; flex-wrap: wrap; gap: var(--td-space-2); }
.td-btn { padding: var(--td-space-2) var(--td-space-4); border: none; border-radius: var(--td-radius-md); font-size: var(--td-font-sm); font-weight: 600; cursor: pointer; }
.td-btn--sm { padding: var(--td-space-1) var(--td-space-3); }
.td-btn--primary { background: var(--td-color-primary); color: var(--td-text-inverse); }
.td-btn--primary:hover { background: var(--td-color-primary-hover); }
.td-btn--ghost { background: var(--td-surface-secondary); color: var(--td-text-secondary); border: 1px solid var(--td-border-default); }
.td-btn--ghost:hover { background: var(--td-surface-tertiary); }
.td-timeline { display: flex; flex-direction: column; gap: 0; }
.td-timeline--virtual { max-height: 600px; overflow-y: auto; contain: strict; }
.td-timeline__entry { display: flex; gap: var(--td-space-4); padding: var(--td-space-4) 0; border-left: 2px solid var(--td-border-default); margin-left: var(--td-space-3); padding-left: var(--td-space-4); position: relative; }
.td-timeline__dot { position: absolute; left: -6px; top: var(--td-space-5); width: 10px; height: 10px; background: var(--td-color-primary); border-radius: 50%; border: 2px solid var(--td-surface-secondary); }
.td-timeline__content { flex: 1; background: var(--td-surface-primary); border-radius: var(--td-radius-md); padding: var(--td-space-3); border: 1px solid var(--td-border-default); }
.td-timeline__header { display: flex; justify-content: space-between; align-items: center; margin-bottom: var(--td-space-1); }
.td-timeline__action { font-weight: 600; font-size: var(--td-font-sm); color: var(--td-text-primary); }
.td-timeline__time { font-size: var(--td-font-xs); color: var(--td-text-tertiary); }
.td-timeline__details { font-size: var(--td-font-xs); color: var(--td-text-secondary); display: flex; gap: var(--td-space-2); }
.td-timeline__entity { font-family: monospace; }
.td-timeline__message { font-size: var(--td-font-sm); color: var(--td-text-secondary); margin-top: var(--td-space-2); padding-top: var(--td-space-2); border-top: 1px solid var(--td-border-default); }
</style>
