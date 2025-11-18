<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useBoardStore } from '../store/boardStore'
import ColumnLane from '../components/board/ColumnLane.vue'

const route = useRoute()
const router = useRouter()
const boardStore = useBoardStore()

const newColumnName = ref('')
const showColumnForm = ref(false)

const boardId = ref(route.params.id as string)

onMounted(async () => {
  try {
    await boardStore.fetchBoard(boardId.value)
  } catch (error) {
    console.error('Failed to load board:', error)
  }
})

async function createColumn() {
  if (!newColumnName.value.trim()) return

  try {
    await boardStore.createColumn(boardId.value, {
      name: newColumnName.value,
    })
    newColumnName.value = ''
    showColumnForm.value = false
  } catch (error) {
    console.error('Failed to create column:', error)
  }
}

function goBack() {
  router.push('/boards')
}
</script>

<template>
  <div class="min-h-screen bg-gray-50">
    <!-- Header -->
    <div class="bg-white border-b border-gray-200">
      <div class="max-w-full px-4 sm:px-6 lg:px-8 py-4">
        <div class="flex items-center justify-between">
          <div class="flex items-center gap-4">
            <button
              @click="goBack"
              class="text-gray-600 hover:text-gray-900 transition-colors"
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
            <div v-if="boardStore.currentBoard">
              <h1 class="text-2xl font-bold text-gray-900">
                {{ boardStore.currentBoard.name }}
              </h1>
              <p v-if="boardStore.currentBoard.description" class="text-sm text-gray-600">
                {{ boardStore.currentBoard.description }}
              </p>
            </div>
          </div>

          <button
            @click="showColumnForm = !showColumnForm"
            class="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors"
          >
            + Add Column
          </button>
        </div>

        <!-- Create Column Form -->
        <div v-if="showColumnForm" class="mt-4 bg-gray-50 rounded-lg p-4">
          <form @submit.prevent="createColumn" class="flex gap-3">
            <input
              v-model="newColumnName"
              type="text"
              placeholder="Column name"
              class="flex-1 px-4 py-2 border border-gray-300 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500"
              autofocus
            />
            <button
              type="submit"
              class="px-6 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors"
            >
              Create
            </button>
            <button
              type="button"
              @click="showColumnForm = false"
              class="px-6 py-2 bg-gray-200 text-gray-700 rounded-lg hover:bg-gray-300 transition-colors"
            >
              Cancel
            </button>
          </form>
        </div>
      </div>
    </div>

    <!-- Loading State -->
    <div v-if="boardStore.loading && !boardStore.currentBoard" class="flex justify-center items-center py-12">
      <div class="inline-block animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600"></div>
    </div>

    <!-- Error State -->
    <div v-else-if="boardStore.error" class="max-w-7xl mx-auto px-4 py-8">
      <div class="bg-red-50 border border-red-200 rounded-lg p-4 text-red-700">
        {{ boardStore.error }}
      </div>
    </div>

    <!-- Board Content -->
    <div v-else-if="boardStore.currentBoard" class="h-[calc(100vh-120px)] overflow-x-auto">
      <div class="flex gap-4 p-6 min-h-full">
        <ColumnLane
          v-for="column in boardStore.currentBoard.columns"
          :key="column.id"
          :column="column"
          :cards="boardStore.cardsByColumn.get(column.id) || []"
          :labels="boardStore.currentBoardLabels"
          :board-id="boardId"
        />

        <!-- Empty State -->
        <div
          v-if="boardStore.currentBoard.columns.length === 0"
          class="flex-1 flex flex-col items-center justify-center text-gray-400"
        >
          <svg
            class="w-16 h-16 mb-4"
            fill="none"
            stroke="currentColor"
            viewBox="0 0 24 24"
          >
            <path
              stroke-linecap="round"
              stroke-linejoin="round"
              stroke-width="2"
              d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2"
            />
          </svg>
          <p class="text-lg font-medium">No columns yet</p>
          <p class="text-sm mt-1">Click "Add Column" to get started</p>
        </div>
      </div>
    </div>
  </div>
</template>
