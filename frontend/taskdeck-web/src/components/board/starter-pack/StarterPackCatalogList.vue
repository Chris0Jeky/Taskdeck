<script setup lang="ts">
import type { StarterPackCatalogEntry } from '../../../types/starter-packs'

defineProps<{
  filteredPacks: StarterPackCatalogEntry[]
  catalogEntries: StarterPackCatalogEntry[]
  selectedPackId: string | null
  loadingCatalog: boolean
  catalogLoadError: string | null
  searchQuery: string
}>()

defineEmits<{
  (e: 'update:searchQuery', value: string): void
  (e: 'select', packId: string): void
}>()
</script>

<template>
  <section class="sp-section-border p-6">
    <label for="starter-pack-search" class="sp-label mb-2 block text-sm font-medium">
      Search
    </label>
    <input
      id="starter-pack-search"
      :value="searchQuery"
      type="text"
      placeholder="Search by name, tag, or purpose"
      :disabled="loadingCatalog || catalogLoadError !== null"
      class="sp-input mb-4 w-full rounded-md px-3 py-2 focus:outline-none"
      @input="$emit('update:searchQuery', ($event.target as HTMLInputElement).value)"
    />

    <div v-if="loadingCatalog" class="sp-empty-state rounded-md border border-dashed p-6 text-center">
      <p class="sp-label text-sm font-medium">Loading starter packs...</p>
    </div>

    <div v-else-if="catalogLoadError" class="sp-error-box rounded-md p-6 text-center">
      <p class="text-sm font-medium">{{ catalogLoadError }}</p>
    </div>

    <div v-else-if="catalogEntries.length === 0" class="sp-empty-state rounded-md border border-dashed p-6 text-center">
      <p class="sp-label text-sm font-medium">No starter packs are currently available.</p>
    </div>

    <div v-else-if="filteredPacks.length === 0" class="sp-empty-state rounded-md border border-dashed p-6 text-center">
      <p class="sp-label text-sm font-medium">No starter packs match this search.</p>
      <p class="sp-muted mt-1 text-xs">Try another keyword to view available packs.</p>
    </div>

    <ul v-else class="space-y-3">
      <li v-for="entry in filteredPacks" :key="entry.id">
        <button
          type="button"
          :class="[
            'sp-pack-card w-full rounded-lg border px-4 py-3 text-left transition-colors',
            selectedPackId === entry.id
              ? 'sp-pack-card--selected'
              : ''
          ]"
          @click="$emit('select', entry.id)"
        >
          <div class="flex items-center justify-between gap-3">
            <p class="sp-text-primary text-sm font-semibold">{{ entry.title }}</p>
            <span class="sp-badge rounded px-2 py-0.5 text-xs font-medium">
              {{ entry.manifest.packId }}
            </span>
          </div>
          <p class="sp-text-secondary mt-1 text-sm">{{ entry.summary }}</p>
          <div class="mt-2 flex flex-wrap gap-1">
            <span
              v-for="tag in entry.manifest.tags"
              :key="`${entry.id}-${tag}`"
              class="sp-badge rounded px-2 py-0.5 text-xs"
            >
              #{{ tag }}
            </span>
          </div>
        </button>
      </li>
    </ul>
  </section>
</template>

<style scoped>
@import './starter-pack-tokens.css';
</style>
