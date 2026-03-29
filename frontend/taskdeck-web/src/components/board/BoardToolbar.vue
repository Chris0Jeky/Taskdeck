<script setup lang="ts">
import type { BoardPresenceMember } from '../../types/realtime'

defineProps<{
  boardName: string
  boardDescription: string | null
  isDemoBoard: boolean
  presenceMembers: BoardPresenceMember[]
  showFilterPanel: boolean
  filteredCardCount: number
  totalCardCount: number
}>()

defineEmits<{
  back: []
  toggleFilter: []
  showKeyboardHelp: []
  showLabelManager: []
  showStarterPackCatalog: []
  showBoardSettings: []
  toggleColumnForm: []
}>()
</script>

<template>
  <div class="flex items-center justify-between">
    <div class="flex items-center gap-4">
      <button
        @click="$emit('back')"
        class="text-on-surface/60 hover:text-on-surface transition-colors"
      >
        <svg class="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path
            stroke-linecap="round"
            stroke-linejoin="round"
            stroke-width="2"
            d="M15 19l-7-7 7-7"
          />
        </svg>
      </button>
      <div>
        <div class="flex flex-wrap items-center gap-2">
          <h1 class="text-2xl font-bold text-on-surface">
            {{ boardName }}
          </h1>
          <span
            v-if="isDemoBoard"
            class="inline-flex items-center rounded-full border border-primary-container/30 bg-primary-container/10 px-2 py-0.5 text-xs font-semibold text-primary"
          >
            Demo board
          </span>
        </div>
        <p v-if="boardDescription" class="text-sm text-on-surface/60">
          {{ boardDescription }}
        </p>
        <div class="mt-2 flex flex-wrap items-center gap-2">
          <span class="text-xs font-semibold uppercase tracking-wide text-on-surface/60">Live</span>
          <span
            v-if="presenceMembers.length === 0"
            class="inline-flex items-center rounded-full bg-surface-container-highest px-2 py-0.5 text-xs text-on-surface/60"
          >
            No active collaborators
          </span>
          <span
            v-for="member in presenceMembers"
            :key="member.userId"
            class="inline-flex items-center gap-1 rounded-full bg-primary-container/10 px-2 py-0.5 text-xs text-primary"
            data-presence-user
          >
            <span>{{ member.displayName || member.userId.slice(0, 8) }}</span>
            <span v-if="member.editingCardId" class="font-medium text-[var(--td-color-warning)]">(editing)</span>
          </span>
        </div>
      </div>
    </div>

    <div class="flex items-center gap-2">
      <button
        @click="$emit('toggleFilter')"
        :class="[
          'p-2 border border-outline-variant/15 rounded-lg transition-colors',
          showFilterPanel ? 'bg-primary-container/20 text-primary border-primary-container' : 'text-on-surface/60 hover:bg-surface-bright'
        ]"
        title="Filter Cards (Press f)"
      >
        <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M3 4a1 1 0 011-1h16a1 1 0 011 1v2.586a1 1 0 01-.293.707l-6.414 6.414a1 1 0 00-.293.707V17l-4 4v-6.586a1 1 0 00-.293-.707L3.293 7.293A1 1 0 013 6.586V4z" />
        </svg>
        <span v-if="filteredCardCount < totalCardCount" class="absolute -top-1 -right-1 flex h-5 w-5 items-center justify-center rounded-full bg-primary-container text-[10px] font-bold text-on-primary-container">
          {{ filteredCardCount }}
        </span>
      </button>
      <button
        @click="$emit('showKeyboardHelp')"
        class="p-2 text-on-surface/60 hover:bg-surface-bright border border-outline-variant/15 rounded-lg transition-colors"
        title="Keyboard Shortcuts (Press ?)"
      >
        <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M8.228 9c.549-1.165 2.03-2 3.772-2 2.21 0 4 1.343 4 3 0 1.4-1.278 2.575-3.006 2.907-.542.104-.994.54-.994 1.093m0 3h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
        </svg>
      </button>
      <button
        @click="$emit('showLabelManager')"
        class="px-4 py-2 text-sm font-medium text-on-surface/70 hover:bg-surface-bright border border-outline-variant/15 rounded-lg transition-colors"
        title="Manage Labels"
      >
        <svg class="w-5 h-5 inline-block mr-1" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 7h.01M7 3h5c.512 0 1.024.195 1.414.586l7 7a2 2 0 010 2.828l-7 7a2 2 0 01-2.828 0l-7-7A1.994 1.994 0 013 12V7a4 4 0 014-4z" />
        </svg>
        Labels
      </button>
      <button
        @click="$emit('showStarterPackCatalog')"
        class="px-4 py-2 text-sm font-medium text-on-surface/70 hover:bg-surface-bright border border-outline-variant/15 rounded-lg transition-colors"
        title="Browse Starter Packs"
      >
        <svg class="w-5 h-5 inline-block mr-1" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 6.253v13m0-13C10.832 5.483 9.246 5 7.5 5 4.462 5 2 6.79 2 9v9c0-2.21 2.462-4 5.5-4 1.746 0 3.332.483 4.5 1.253m0-9C13.168 5.483 14.754 5 16.5 5c3.038 0 5.5 1.79 5.5 4v9c0-2.21-2.462-4-5.5-4-1.746 0-3.332.483-4.5 1.253" />
        </svg>
        Starter Packs
      </button>
      <button
        @click="$emit('showBoardSettings')"
        class="px-4 py-2 text-sm font-medium text-on-surface/70 hover:bg-surface-bright border border-outline-variant/15 rounded-lg transition-colors"
        title="Board Settings"
      >
        <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z" />
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
        </svg>
      </button>
      <button
        @click="$emit('toggleColumnForm')"
        class="px-4 py-2 bg-primary-container text-on-primary-container rounded-lg hover:brightness-110 transition-colors"
      >
        + Add Column
      </button>
    </div>
  </div>
</template>
