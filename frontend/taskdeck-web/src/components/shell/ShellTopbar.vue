<script setup lang="ts">
import { computed } from 'vue'
import { useSessionStore } from '../../store/sessionStore'
import { useWorkspaceStore } from '../../store/workspaceStore'
import type { WorkspaceMode } from '../../types/workspace'
import { isWorkspaceMode } from '../../types/workspace'

const emit = defineEmits<{
  'open-command-palette': []
}>()

const session = useSessionStore()
const workspace = useWorkspaceStore()

const workspaceModeMeta: Record<WorkspaceMode, { label: string; description: string }> = {
  guided: {
    label: 'Guided',
    description: 'Keep Home, Review, and board work front and center.',
  },
  workbench: {
    label: 'Workbench',
    description: 'Show the full shipped workspace alongside the core loop, without hiding shipped surfaces behind feature flags.',
  },
  agent: {
    label: 'Agent',
    description: 'Hold the same review-first path while agent surfaces are staged in later work.',
  },
}

const activeWorkspaceMode = computed<WorkspaceMode>(() =>
  isWorkspaceMode(workspace.mode)
    ? workspace.mode
    : 'guided')

const currentModeMeta = computed(() => workspaceModeMeta[activeWorkspaceMode.value])

function handleWorkspaceModeChange(event: Event) {
  const nextMode = (event.target as HTMLSelectElement | null)?.value
  if (!nextMode || !isWorkspaceMode(nextMode)) {
    return
  }

  void workspace.updateMode(nextMode)
}
</script>

<template>
  <header class="td-topbar" role="banner">
    <div class="td-topbar__left">
      <div class="td-topbar__mode">
        <label class="td-topbar__mode-label" for="workspace-mode-select">Workspace</label>
        <div class="td-topbar__mode-controls">
          <select
            id="workspace-mode-select"
            class="td-topbar__mode-select"
            :value="activeWorkspaceMode"
            aria-label="Workspace mode"
            @change="handleWorkspaceModeChange"
          >
            <option value="guided">Guided</option>
            <option value="workbench">Workbench</option>
            <option value="agent">Agent</option>
          </select>
          <span class="td-topbar__mode-copy">{{ currentModeMeta.description }}</span>
        </div>
      </div>

      <button
        class="td-topbar__palette-trigger"
        aria-label="Open command palette (Ctrl+K)"
        @click="emit('open-command-palette')"
      >
        <span class="material-symbols-outlined td-topbar__search-icon">search</span>
        <span class="td-topbar__search-text">Go anywhere... (Ctrl+K)</span>
      </button>
    </div>

    <div class="td-topbar__right">
      <div class="td-topbar__status">
        <span class="td-topbar__status-dot"></span>
        <span class="td-topbar__status-label">System Live</span>
      </div>
      <span v-if="session.isAuthenticated" class="td-topbar__user">
        {{ session.username }}
      </span>
    </div>
  </header>
</template>

<style scoped>
.td-topbar {
  background: var(--td-surface-base);
  border-bottom: 1px solid rgba(91, 64, 62, 0.15);
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 var(--td-space-8);
  flex-shrink: 0;
  gap: var(--td-space-5);
  height: var(--td-topbar-height);
}

.td-topbar__left {
  flex: 1;
  display: flex;
  align-items: center;
  gap: var(--td-space-8);
}

.td-topbar__mode {
  display: flex;
  align-items: center;
  gap: var(--td-space-3);
}

.td-topbar__mode-label {
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: 10px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.2em;
  color: var(--td-text-tertiary);
}

.td-topbar__mode-controls {
  display: flex;
  align-items: center;
  gap: var(--td-space-3);
}

.td-topbar__mode-select {
  border: 0.5px solid rgba(91, 64, 62, 0.2);
  background: var(--td-surface-container-lowest);
  color: var(--td-text-primary);
  padding: 0.35rem 0.75rem;
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: 11px;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  min-width: 120px;
}

.td-topbar__mode-select:focus {
  border-color: var(--td-color-ember-glow);
}

.td-topbar__mode-select option {
  background: var(--td-surface-container);
  color: var(--td-text-primary);
}

.td-topbar__mode-copy {
  font-size: var(--td-font-xs);
  color: var(--td-text-tertiary);
  max-width: 300px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.td-topbar__palette-trigger {
  display: flex;
  align-items: center;
  gap: var(--td-space-3);
  padding: var(--td-space-3) var(--td-space-5);
  background: var(--td-surface-container-low);
  border: 0.5px solid rgba(91, 64, 62, 0.15);
  color: var(--td-text-tertiary);
  cursor: pointer;
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: 11px;
  letter-spacing: 0.05em;
  min-width: 260px;
  width: fit-content;
  transition: border-color var(--td-transition-fast);
}

.td-topbar__palette-trigger:hover {
  border-color: var(--td-border-focus);
}

.td-topbar__search-icon {
  font-size: 16px;
  color: var(--td-text-tertiary);
}

.td-topbar__search-text {
  color: var(--td-text-tertiary);
}

.td-topbar__right {
  display: flex;
  align-items: center;
  gap: var(--td-space-5);
}

.td-topbar__status {
  display: flex;
  align-items: center;
  gap: var(--td-space-2);
}

.td-topbar__status-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--td-color-ember);
  animation: ember-pulse 2s infinite;
}

.td-topbar__status-label {
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: 10px;
  font-weight: 700;
  letter-spacing: 0.15em;
  text-transform: uppercase;
  color: var(--td-text-tertiary);
}

.td-topbar__user {
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: 11px;
  color: var(--td-text-muted);
  font-weight: 500;
  letter-spacing: 0.05em;
}

@media (max-width: 900px) {
  .td-topbar {
    flex-direction: column;
    align-items: stretch;
    height: auto;
    padding: var(--td-space-3) var(--td-space-5);
  }

  .td-topbar__left {
    flex-direction: column;
    gap: var(--td-space-3);
  }

  .td-topbar__palette-trigger {
    width: 100%;
    min-width: 0;
  }

  .td-topbar__right {
    justify-content: space-between;
  }
}
</style>
