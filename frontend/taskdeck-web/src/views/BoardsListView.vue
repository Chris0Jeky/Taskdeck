<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useBoardStore } from '../store/boardStore'

const router = useRouter()
const boardStore = useBoardStore()

const newBoardName = ref('')
const showCreateForm = ref(false)

onMounted(async () => {
  await boardStore.fetchBoards()
})

async function createBoard() {
  if (!newBoardName.value.trim()) return

  try {
    const board = await boardStore.createBoard({
      name: newBoardName.value,
    })

    newBoardName.value = ''
    showCreateForm.value = false

    // Navigate to the new board
    router.push(`/boards/${board.id}`)
  } catch (error) {
    console.error('Failed to create board:', error)
  }
}

function goToBoard(id: string) {
  router.push(`/boards/${id}`)
}
</script>

<template>
  <div class="min-h-screen bg-gray-50">
    <div class="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-8">
      <div class="flex justify-between items-center mb-8">
        <h1 class="text-3xl font-bold text-gray-900">My Boards</h1>
        <button
          @click="showCreateForm = !showCreateForm"
          class="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors"
        >
          + New Board
        </button>
      </div>

      <!-- Create Board Form -->
      <div v-if="showCreateForm" class="mb-6 bg-white rounded-lg shadow-sm p-6">
        <h2 class="text-lg font-semibold mb-4">Create New Board</h2>
        <form @submit.prevent="createBoard" class="flex gap-3">
          <input
            v-model="newBoardName"
            type="text"
            placeholder="Board name"
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
            @click="showCreateForm = false"
            class="px-6 py-2 bg-gray-200 text-gray-700 rounded-lg hover:bg-gray-300 transition-colors"
          >
            Cancel
          </button>
        </form>
      </div>

      <!-- Loading State -->
      <div v-if="boardStore.loading" class="text-center py-12">
        <div class="inline-block animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600"></div>
        <p class="mt-4 text-gray-600">Loading boards...</p>
      </div>

      <!-- Error State -->
      <div v-else-if="boardStore.error" class="bg-red-50 border border-red-200 rounded-lg p-4 text-red-700">
        {{ boardStore.error }}
      </div>

      <!-- Empty State -->
      <div v-else-if="boardStore.boards.length === 0" class="text-center py-12">
        <svg
          class="mx-auto h-12 w-12 text-gray-400"
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
        <h3 class="mt-2 text-sm font-medium text-gray-900">No boards</h3>
        <p class="mt-1 text-sm text-gray-500">Get started by creating a new board.</p>
        <div class="mt-6">
          <button
            @click="showCreateForm = true"
            class="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors"
          >
            + Create Board
          </button>
        </div>
      </div>

      <!-- Boards Grid -->
      <div v-else class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
        <div
          v-for="board in boardStore.boards"
          :key="board.id"
          @click="goToBoard(board.id)"
          class="bg-white rounded-lg shadow-sm hover:shadow-md transition-shadow cursor-pointer p-6 border border-gray-200"
        >
          <h3 class="text-xl font-semibold text-gray-900 mb-2">
            {{ board.name }}
          </h3>
          <p v-if="board.description" class="text-gray-600 text-sm line-clamp-2">
            {{ board.description }}
          </p>
          <div v-else class="text-gray-400 text-sm italic">No description</div>
          <div class="mt-4 text-xs text-gray-500">
            Created {{ new Date(board.createdAt).toLocaleDateString() }}
          </div>
        </div>
      </div>
    </div>
  </div>
</template>
