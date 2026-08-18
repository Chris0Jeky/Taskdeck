<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, type RouteLocationMatched } from 'vue-router'
import { useSessionStore } from '../../store/sessionStore'
import { useWorkspaceStore } from '../../store/workspaceStore'
import type { WorkspaceMode } from '../../types/workspace'
import { isWorkspaceMode } from '../../types/workspace'
import PaperIcon from './PaperIcon.vue'
import PaperKbd from './PaperKbd.vue'
import PaperStatusPill from './PaperStatusPill.vue'

/**
 * PaperTopBar — Paper & Graphite top bar.
 *
 * Mirrors the JSX in `design_handoff_taskdeck_paper/paper/components.jsx`
 * (`TopBar`).  Builds breadcrumb segments from `route.matched`, looking for
 * `route.meta.breadcrumb` first and otherwise humanizing the route name.  The
 * ⌘K trigger emits `palette:open` so the parent shell can wire it into the
 * existing command-palette composable.  Bell + Settings render as ghost icon
 * buttons; the avatar uses the first letter of the session username.
 */

const emit = defineEmits<{
  'palette:open': []
}>()

const route = useRoute()
const session = useSessionStore()
const workspace = useWorkspaceStore()

const workspaceModeMeta: Record<WorkspaceMode, { label: string; description: string }> = {
  guided: {
    label: 'Guided',
    description: 'Core loop',
  },
  workbench: {
    label: 'Workbench',
    description: 'Full workspace',
  },
  agent: {
    label: 'Agent',
    description: 'Agent surfaces',
  },
}

type Crumb = {
  label: string
  path: string
  isLast: boolean
}

function humanize(value: unknown): string {
  if (typeof value !== 'string' || value.trim() === '') return ''
  return value
    .replace(/^workspace[-/]?/i, '')
    .replace(/[-_/]+/g, ' ')
    .replace(/\b\w/g, (ch) => ch.toUpperCase())
    .trim()
}

function crumbLabelFor(matched: RouteLocationMatched): string {
  const meta = matched.meta as Record<string, unknown> | undefined
  const metaCrumb = meta?.breadcrumb
  if (typeof metaCrumb === 'string' && metaCrumb.trim() !== '') return metaCrumb
  if (typeof matched.name === 'string') {
    const humanized = humanize(matched.name)
    if (humanized) return humanized
  }
  return humanize(matched.path) || 'Workspace'
}

const crumbs = computed<Crumb[]>(() => {
  const matched = (route.matched ?? []).filter((m) => {
    if (!m.path) return false
    // Skip layout/redirect routes that do not describe a breadcrumb segment.
    if (m.path === '/' || m.path === '') return false
    return true
  })
  if (matched.length === 0) {
    return [{ label: 'Workspace', path: '/workspace', isLast: true }]
  }
  return matched.map((m, i) => ({
    label: crumbLabelFor(m),
    path: m.path,
    isLast: i === matched.length - 1,
  }))
})

const avatarLetter = computed(() => {
  const name = session.username || 'D'
  return name.trim().charAt(0).toUpperCase() || 'D'
})

const activeWorkspaceMode = computed<WorkspaceMode>(() =>
  isWorkspaceMode(workspace.mode)
    ? workspace.mode
    : 'guided')

const currentModeMeta = computed(() => workspaceModeMeta[activeWorkspaceMode.value])

const commandModifierLabel = computed(() => {
  if (typeof navigator === 'undefined') return 'Ctrl'
  return /Mac|iPhone|iPad|iPod/i.test(navigator.platform) ? '⌘' : 'Ctrl'
})

function handlePaletteClick() {
  emit('palette:open')
}

function handleWorkspaceModeChange(event: Event) {
  const nextMode = (event.target as HTMLSelectElement | null)?.value
  if (!nextMode || !isWorkspaceMode(nextMode)) {
    return
  }

  void workspace.updateMode(nextMode)
}
</script>

<template>
  <header class="paper-topbar" data-paper-topbar>
    <nav class="paper-topbar__crumbs" aria-label="Breadcrumb">
      <template v-for="(c, i) in crumbs" :key="`${c.path}-${i}`">
        <span
          class="paper-topbar__crumb"
          :class="{ 'paper-topbar__crumb--last': c.isLast }"
          :aria-current="c.isLast ? 'page' : undefined"
        >{{ c.label }}</span>
        <span v-if="!c.isLast" class="paper-topbar__sep" aria-hidden="true">/</span>
      </template>
    </nav>

    <div class="paper-topbar__spacer" />

    <label class="paper-topbar__mode">
      <span class="tk-eyebrow paper-topbar__mode-label">Workspace</span>
      <select
        class="paper-topbar__mode-select"
        :value="activeWorkspaceMode"
        aria-label="Workspace mode"
        :title="currentModeMeta.description"
        @change="handleWorkspaceModeChange"
      >
        <option value="guided">Guided</option>
        <option value="workbench">Workbench</option>
        <option value="agent">Agent</option>
      </select>
    </label>

    <button
      type="button"
      class="paper-topbar__palette"
      aria-label="Open command palette"
      @click="handlePaletteClick"
    >
      <PaperIcon name="search" />
      <span class="paper-topbar__palette-label">Go anywhere &middot; capture &middot; ask</span>
      <span class="paper-topbar__palette-spacer" />
      <PaperKbd>{{ commandModifierLabel }}</PaperKbd>
      <PaperKbd>K</PaperKbd>
    </button>

    <PaperStatusPill kind="live" class="paper-topbar__status">SYNCED &middot; LOCAL-FIRST</PaperStatusPill>

    <div class="paper-topbar__hairline" aria-hidden="true" />

    <button type="button" class="paper-topbar__icon-btn" aria-label="Notifications">
      <PaperIcon name="bell" />
    </button>
    <button type="button" class="paper-topbar__icon-btn" aria-label="Settings">
      <PaperIcon name="settings" />
    </button>

    <div class="paper-topbar__avatar" :aria-label="`Profile: ${avatarLetter}`">{{ avatarLetter }}</div>
  </header>
</template>

<style scoped>
.paper-topbar {
  height: 48px;
  border-bottom: 1px solid var(--line);
  display: flex;
  align-items: center;
  padding: 0 20px;
  background: var(--paper);
  gap: 18px;
  position: relative;
  flex: none;
}

.paper-topbar__crumbs {
  display: flex;
  align-items: center;
  gap: 8px;
  font-family: var(--sans);
  font-size: 12.5px;
  min-width: 0;
  overflow: hidden;
}

.paper-topbar__crumb {
  color: var(--mute);
  font-weight: 400;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 22ch;
}

.paper-topbar__crumb--last {
  color: var(--ink);
  font-weight: 500;
}

.paper-topbar__sep {
  color: var(--whisper);
}

.paper-topbar__spacer {
  flex: 1;
}

.paper-topbar__mode {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}

.paper-topbar__mode-label {
  color: var(--faint);
  white-space: nowrap;
}

.paper-topbar__mode-select {
  height: 32px;
  border: 1px solid var(--line);
  border-radius: 4px;
  background: var(--paper-card);
  color: var(--ink);
  font-family: var(--sans);
  font-size: 12px;
  padding: 0 8px;
  cursor: pointer;
}

.paper-topbar__mode-select:focus {
  border-color: var(--ember);
  outline: none;
}

.paper-topbar__palette {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 5px 10px 5px 8px;
  border: 1px solid var(--line);
  border-radius: 4px;
  background: var(--paper-card);
  font-family: var(--sans);
  font-size: 12px;
  color: var(--mute);
  cursor: pointer;
  min-width: 320px;
  height: 32px;
}

.paper-topbar__palette:hover {
  border-color: var(--ember);
}

.paper-topbar__palette-label {
  color: var(--mute);
}

.paper-topbar__palette-spacer {
  flex: 1;
}

.paper-topbar__hairline {
  width: 1px;
  height: 18px;
  background: var(--line);
}

.paper-topbar__icon-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 6px;
  background: transparent;
  border: 1px solid transparent;
  border-radius: 4px;
  color: var(--ink-2);
  cursor: pointer;
}

.paper-topbar__icon-btn:hover {
  background: var(--paper-2);
  color: var(--ember);
}

.paper-topbar__avatar {
  width: 26px;
  height: 26px;
  border-radius: 50%;
  border: 1px solid var(--line);
  background: var(--paper-card);
  display: grid;
  place-items: center;
  font-family: var(--serif);
  font-style: italic;
  font-size: 13px;
  color: var(--ink-deep);
}

@media (max-width: 640px) {
  .paper-topbar {
    padding: 0 12px;
    gap: 8px;
  }

  .paper-topbar__crumbs {
    flex: 0 1 auto;
    max-width: min(32vw, 140px);
    gap: 4px;
  }

  .paper-topbar__crumb {
    max-width: 12ch;
  }

  .paper-topbar__spacer,
  .paper-topbar__mode-label,
  .paper-topbar__palette-label,
  .paper-topbar__palette-spacer,
  .paper-topbar__status,
  .paper-topbar__hairline,
  .paper-topbar__icon-btn {
    display: none;
  }

  .paper-topbar__mode {
    flex: 0 0 auto;
  }

  .paper-topbar__mode-select {
    max-width: 82px;
    padding: 0 4px;
  }

  .paper-topbar__palette {
    flex: 1 1 auto;
    min-width: 0;
    width: auto;
    gap: 6px;
    padding: 5px 6px;
  }

  .paper-topbar__avatar {
    flex: 0 0 26px;
  }
}
</style>
