import { defineStore } from 'pinia'
import { ref, watch } from 'vue'

export type WorkspaceMode = 'guided' | 'workbench' | 'agent'

const STORAGE_KEY = 'taskdeck_workspace_mode'

export const useWorkspaceModeStore = defineStore('workspaceMode', () => {
  const mode = ref<WorkspaceMode>('guided')

  function restore() {
    const saved = localStorage.getItem(STORAGE_KEY)
    if (saved === 'guided' || saved === 'workbench' || saved === 'agent') {
      mode.value = saved
    }
  }

  function setMode(next: WorkspaceMode) {
    mode.value = next
  }

  watch(mode, (value) => {
    localStorage.setItem(STORAGE_KEY, value)
  }, { immediate: true })

  return {
    mode,
    restore,
    setMode,
  }
})
