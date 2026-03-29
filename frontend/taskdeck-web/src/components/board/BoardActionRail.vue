<script setup lang="ts">
defineEmits<{
  capture: []
  chat: []
  review: []
  inbox: []
  addCard: []
}>()
</script>

<template>
  <div
    class="mt-4 flex flex-wrap items-center gap-2 rounded-xl border border-outline-variant/15 bg-surface-container-low px-4 py-3"
    data-board-action-rail
  >
    <span class="text-xs font-semibold uppercase tracking-wide text-on-surface/60">Board Actions</span>
    <button
      class="px-3 py-2 text-sm font-medium text-on-surface/70 hover:bg-surface-container border border-outline-variant/15 rounded-lg transition-colors"
      @click="$emit('capture')"
    >
      Capture here
    </button>
    <button
      class="px-3 py-2 text-sm font-medium text-on-surface/70 hover:bg-surface-container border border-outline-variant/15 rounded-lg transition-colors"
      @click="$emit('chat')"
    >
      Ask assistant
    </button>
    <button
      class="px-3 py-2 text-sm font-medium text-on-surface/70 hover:bg-surface-container border border-outline-variant/15 rounded-lg transition-colors"
      @click="$emit('review')"
    >
      Review proposals
    </button>
    <button
      class="px-3 py-2 text-sm font-medium text-on-surface/70 hover:bg-surface-container border border-outline-variant/15 rounded-lg transition-colors"
      @click="$emit('inbox')"
    >
      Open Inbox
    </button>
    <button
      class="px-3 py-2 text-sm font-medium text-on-primary-container bg-primary-container hover:brightness-110 rounded-lg transition-colors"
      @click="$emit('addCard')"
    >
      Add card
    </button>
    <p class="text-sm text-on-surface/60">
      New input starts in Capture or Inbox. Only approved changes land on this board.
    </p>
  </div>
</template>
