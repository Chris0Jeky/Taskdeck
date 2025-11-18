<script setup lang="ts">
import { ref } from 'vue'
import { useBoardStore } from '../../store/boardStore'
import CardItem from './CardItem.vue'
import type { Column, Card, Label } from '../../types/board'

const props = defineProps<{
  column: Column
  cards: Card[]
  labels: Label[]
  boardId: string
}>()

const boardStore = useBoardStore()
const newCardTitle = ref('')
const showCardForm = ref(false)

async function createCard() {
  if (!newCardTitle.value.trim()) return

  try {
    await boardStore.createCard(props.boardId, {
      columnId: props.column.id,
      title: newCardTitle.value,
    })

    newCardTitle.value = ''
    showCardForm.value = false
  } catch (error) {
    console.error('Failed to create card:', error)
  }
}

const isWipLimitExceeded = () => {
  return props.column.wipLimit !== null && props.cards.length > props.column.wipLimit
}
</script>

<template>
  <div class="flex-shrink-0 w-80 bg-gray-100 rounded-lg p-4">
    <!-- Column Header -->
    <div class="mb-4">
      <div class="flex items-center justify-between mb-2">
        <h3 class="font-semibold text-gray-900">{{ column.name }}</h3>
        <div class="flex items-center gap-2">
          <span
            class="text-sm px-2 py-1 rounded"
            :class="isWipLimitExceeded() ? 'bg-red-100 text-red-700' : 'bg-gray-200 text-gray-700'"
          >
            {{ cards.length }}{{ column.wipLimit ? `/${column.wipLimit}` : '' }}
          </span>
        </div>
      </div>

      <div v-if="isWipLimitExceeded()" class="text-xs text-red-600 mb-2">
        ⚠️ WIP limit exceeded
      </div>

      <button
        @click="showCardForm = !showCardForm"
        class="w-full px-3 py-2 text-sm text-gray-700 hover:bg-gray-200 rounded transition-colors flex items-center justify-center gap-1"
      >
        <span>+</span>
        <span>Add Card</span>
      </button>

      <!-- Create Card Form -->
      <div v-if="showCardForm" class="mt-3 bg-white rounded-lg p-3 shadow-sm">
        <form @submit.prevent="createCard">
          <textarea
            v-model="newCardTitle"
            placeholder="Enter card title..."
            class="w-full px-3 py-2 border border-gray-300 rounded resize-none focus:outline-none focus:ring-2 focus:ring-blue-500"
            rows="3"
            autofocus
          ></textarea>
          <div class="flex gap-2 mt-2">
            <button
              type="submit"
              class="px-3 py-1.5 bg-blue-600 text-white text-sm rounded hover:bg-blue-700 transition-colors"
            >
              Add
            </button>
            <button
              type="button"
              @click="showCardForm = false"
              class="px-3 py-1.5 bg-gray-200 text-gray-700 text-sm rounded hover:bg-gray-300 transition-colors"
            >
              Cancel
            </button>
          </div>
        </form>
      </div>
    </div>

    <!-- Cards List -->
    <div class="space-y-2 overflow-y-auto max-h-[calc(100vh-280px)]">
      <CardItem
        v-for="card in cards"
        :key="card.id"
        :card="card"
      />

      <!-- Empty State -->
      <div v-if="cards.length === 0 && !showCardForm" class="text-center py-8 text-gray-400 text-sm">
        No cards yet
      </div>
    </div>
  </div>
</template>
