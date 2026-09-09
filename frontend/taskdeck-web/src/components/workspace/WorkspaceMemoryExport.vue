<script setup lang="ts">
import { ref } from 'vue'
import { useSessionStore } from '../../store/sessionStore'
import { getErrorDisplay } from '../../composables/useErrorMapper'
import { collectWorkspaceMemoryExport, downloadWorkspaceMemoryJson } from '../../utils/workspaceMemoryExport'
import PaperHLBtn from '../paper/PaperHLBtn.vue'

const props = defineProps<{ boardId: string; disabled?: boolean }>()
const busy = ref(false)
const error = ref<string | null>(null)
const message = ref<string | null>(null)

async function download() {
  if (!props.boardId || props.disabled || busy.value) return
  const session = useSessionStore()
  const userId = session.userId
  const token = session.token
  const boardId = props.boardId
  const current = () => Boolean(userId && token && session.userId === userId && session.token === token && props.boardId === boardId)
  busy.value = true
  error.value = null
  message.value = null
  try {
    const json = await collectWorkspaceMemoryExport(boardId, current)
    if (!current()) throw new Error('The selected board or session changed. Start the export again.')
    downloadWorkspaceMemoryJson(json, boardId)
    message.value = 'Download requested. Your saved active and archived memories are included; unsaved text stays here.'
  } catch (failure) {
    error.value = getErrorDisplay(failure, 'Memory could not be downloaded. Your draft is unchanged; try again.').message
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="memory-export">
    <PaperHLBtn :disabled="!boardId || disabled || busy" @click="download">{{ busy ? 'Preparing memory…' : 'Download memory JSON' }}</PaperHLBtn>
    <small>Private, user-initiated export of your saved memories for this board, including archived entries.</small>
    <p v-if="error" role="alert">{{ error }}</p>
    <p v-if="message" role="status">{{ message }}</p>
  </div>
</template>

<style scoped>
.memory-export { display: grid; gap: .45rem; justify-items: start; color: var(--td-text-secondary); }
.memory-export small,.memory-export p { font-size: .75rem; margin: 0; max-width: 42rem; }
.memory-export [role=alert] { color: var(--td-color-error, #a33); }
</style>
