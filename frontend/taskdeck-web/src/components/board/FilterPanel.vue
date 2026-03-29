<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import type { Label } from '../../types/board'

export interface CardFilters {
  searchText: string
  labelIds: string[]
  dueDateFilter: 'all' | 'overdue' | 'due-today' | 'due-week' | 'no-date'
  showBlockedOnly: boolean
}

const props = defineProps<{
  isOpen: boolean
  labels: Label[]
  activeFilters: CardFilters
}>()

const emit = defineEmits<{
  (e: 'update:filters', filters: CardFilters): void
  (e: 'toggle'): void
}>()

const localFilters = ref<CardFilters>({
  ...props.activeFilters,
  labelIds: [...props.activeFilters.labelIds],
})

watch(
  () => props.activeFilters,
  (nextFilters) => {
    localFilters.value = {
      ...nextFilters,
      labelIds: [...nextFilters.labelIds],
    }
  },
  { deep: true }
)

const updateFilters = () => {
  emit('update:filters', {
    ...localFilters.value,
    labelIds: [...localFilters.value.labelIds],
  })
}

const toggleLabel = (labelId: string) => {
  const index = localFilters.value.labelIds.indexOf(labelId)
  if (index > -1) {
    localFilters.value.labelIds.splice(index, 1)
  } else {
    localFilters.value.labelIds.push(labelId)
  }
  updateFilters()
}

const isLabelSelected = (labelId: string) => {
  return localFilters.value.labelIds.includes(labelId)
}

const clearAllFilters = () => {
  localFilters.value = {
    searchText: '',
    labelIds: [],
    dueDateFilter: 'all',
    showBlockedOnly: false
  }
  updateFilters()
}

const hasActiveFilters = computed(() => {
  return (
    localFilters.value.searchText !== '' ||
    localFilters.value.labelIds.length > 0 ||
    localFilters.value.dueDateFilter !== 'all' ||
    localFilters.value.showBlockedOnly
  )
})
</script>

<template>
  <div
    v-show="isOpen"
    class="bg-surface-container-low border-b border-outline-variant/30 shadow-sm"
  >
    <div class="max-w-full px-4 sm:px-6 lg:px-8 py-4">
      <div class="flex items-center justify-between mb-4">
        <h3 class="text-lg font-semibold text-on-surface flex items-center gap-2">
          <svg class="w-5 h-5 text-primary" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M3 4a1 1 0 011-1h16a1 1 0 011 1v2.586a1 1 0 01-.293.707l-6.414 6.414a1 1 0 00-.293.707V17l-4 4v-6.586a1 1 0 00-.293-.707L3.293 7.293A1 1 0 013 6.586V4z" />
          </svg>
          Filter Cards
        </h3>
        <div class="flex items-center gap-2">
          <button
            v-if="hasActiveFilters"
            @click="clearAllFilters"
            class="px-3 py-1.5 text-sm text-on-surface-variant hover:text-on-surface hover:bg-surface-container-high rounded transition-colors"
          >
            Clear all
          </button>
          <button
            @click="emit('toggle')"
            class="p-1 text-on-surface-variant hover:text-on-surface rounded hover:bg-surface-container-high transition-colors"
            aria-label="Close filters"
          >
            <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>
      </div>

      <div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
        <div class="space-y-2">
          <label class="block text-sm font-medium text-on-surface-variant">Search</label>
          <input
            v-model="localFilters.searchText"
            @input="updateFilters"
            type="text"
            placeholder="Search cards..."
            class="w-full px-3 py-2 bg-surface-container-high border border-outline-variant/40 rounded-lg text-on-surface placeholder-on-surface-variant/50 focus:outline-none focus:ring-2 focus:ring-primary/50 text-sm"
          />
        </div>

        <div class="space-y-2">
          <label class="block text-sm font-medium text-on-surface-variant">Due Date</label>
          <select
            v-model="localFilters.dueDateFilter"
            @change="updateFilters"
            class="w-full px-3 py-2 bg-surface-container-high border border-outline-variant/40 rounded-lg text-on-surface focus:outline-none focus:ring-2 focus:ring-primary/50 text-sm"
          >
            <option value="all">All cards</option>
            <option value="overdue">Overdue</option>
            <option value="due-today">Due today</option>
            <option value="due-week">Due this week</option>
            <option value="no-date">No due date</option>
          </select>
        </div>

        <div class="space-y-2">
          <label class="block text-sm font-medium text-on-surface-variant">Status</label>
          <label class="flex items-center px-3 py-2 bg-surface-container-high border border-outline-variant/40 rounded-lg cursor-pointer hover:bg-surface-container-highest transition-colors">
            <input
              v-model="localFilters.showBlockedOnly"
              @change="updateFilters"
              type="checkbox"
              class="w-4 h-4 text-primary border-outline-variant rounded focus:ring-primary/50"
            />
            <span class="ml-2 text-sm text-on-surface">Show blocked only</span>
          </label>
        </div>

        <div class="space-y-2">
          <label class="block text-sm font-medium text-on-surface-variant">
            Labels
            <span v-if="localFilters.labelIds.length > 0" class="text-xs text-on-surface-variant/70">
              ({{ localFilters.labelIds.length }} selected)
            </span>
          </label>
          <div class="max-h-[120px] overflow-y-auto bg-surface-container-high border border-outline-variant/40 rounded-lg p-2 space-y-1">
            <div v-if="labels.length === 0" class="text-sm text-on-surface-variant py-1">
              No labels available
            </div>
            <label
              v-for="label in labels"
              :key="label.id"
              class="flex items-center px-2 py-1.5 rounded hover:bg-surface-container-highest cursor-pointer transition-colors"
            >
              <input
                :checked="isLabelSelected(label.id)"
                @change="toggleLabel(label.id)"
                type="checkbox"
                class="w-4 h-4 text-primary border-outline-variant rounded focus:ring-primary/50"
              />
              <span
                class="ml-2 px-2 py-0.5 rounded text-xs font-medium text-white"
                :style="{ backgroundColor: label.colorHex }"
              >
                {{ label.name }}
              </span>
            </label>
          </div>
        </div>
      </div>

      <div v-if="hasActiveFilters" class="mt-4 flex items-center gap-2 flex-wrap">
        <span class="text-sm text-on-surface-variant">Active filters:</span>
        <span v-if="localFilters.searchText" class="inline-flex items-center gap-1 px-2 py-1 bg-primary/20 text-primary text-xs rounded">
          Search: "{{ localFilters.searchText }}"
          <button @click="localFilters.searchText = ''; updateFilters()" class="hover:text-primary/70">&times;</button>
        </span>
        <span v-if="localFilters.dueDateFilter !== 'all'" class="inline-flex items-center gap-1 px-2 py-1 bg-primary/20 text-primary text-xs rounded">
          {{ localFilters.dueDateFilter }}
          <button @click="localFilters.dueDateFilter = 'all'; updateFilters()" class="hover:text-primary/70">&times;</button>
        </span>
        <span v-if="localFilters.showBlockedOnly" class="inline-flex items-center gap-1 px-2 py-1 bg-primary/20 text-primary text-xs rounded">
          Blocked only
          <button @click="localFilters.showBlockedOnly = false; updateFilters()" class="hover:text-primary/70">&times;</button>
        </span>
        <span
          v-for="labelId in localFilters.labelIds"
          :key="labelId"
          class="inline-flex items-center gap-1 px-2 py-1 bg-primary/20 text-primary text-xs rounded"
        >
          {{ labels.find(l => l.id === labelId)?.name }}
          <button @click="toggleLabel(labelId)" class="hover:text-primary/70">&times;</button>
        </span>
      </div>
    </div>
  </div>
</template>
