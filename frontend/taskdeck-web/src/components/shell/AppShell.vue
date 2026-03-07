<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useFeatureFlagStore } from '../../store/featureFlagStore'
import { useSessionStore } from '../../store/sessionStore'
import { useWorkspaceStore } from '../../store/workspaceStore'
import { registerEscapeHandler } from '../../composables/useEscapeStack'
import CaptureModal from '../common/CaptureModal.vue'
import type { WorkspaceMode } from '../../types/workspace'

const router = useRouter()
const route = useRoute()
const session = useSessionStore()
const featureFlags = useFeatureFlagStore()
const workspace = useWorkspaceStore()

const sidebarCollapsed = ref(false)
const showCommandPalette = ref(false)
const showKeyboardHelp = ref(false)
const showCaptureModal = ref(false)
const commandPaletteInput = ref<HTMLInputElement | null>(null)
const commandQuery = ref('')
const selectedCommandIndex = ref(0)
const commandListboxId = 'td-command-palette-listbox'

type NavItem = {
  id: string
  label: string
  icon: string
  path: string
  flag: string | null
  primaryModes: WorkspaceMode[]
  secondaryModes?: WorkspaceMode[]
  keywords?: string
}

type CommandItem = {
  id: string
  label: string
  icon: string
  path?: string
  keywords?: string
  kind: 'navigation' | 'action'
  action?: () => void
}

const workspaceModeMeta: Record<WorkspaceMode, { label: string; description: string }> = {
  guided: {
    label: 'Guided',
    description: 'Keep Home, Review, and board work front and center.',
  },
  workbench: {
    label: 'Workbench',
    description: 'Keep every workspace surface visible for hands-on work.',
  },
  agent: {
    label: 'Agent',
    description: 'Hold the same review-first path while agent surfaces are staged in later work.',
  },
}

const supportedWorkspaceModes = ['guided', 'workbench', 'agent'] as const

function isWorkspaceMode(value: string): value is WorkspaceMode {
  return supportedWorkspaceModes.includes(value as WorkspaceMode)
}

const navCatalog: NavItem[] = [
  {
    id: 'home',
    label: 'Home',
    icon: 'H',
    path: '/workspace/home',
    flag: null,
    primaryModes: ['guided', 'workbench', 'agent'],
    keywords: 'home start summary workspace',
  },
  {
    id: 'review',
    label: 'Review',
    icon: 'R',
    path: '/workspace/automations/proposals',
    flag: 'newAutomation',
    primaryModes: ['guided', 'workbench', 'agent'],
    keywords: 'review proposals automations approve reject execute',
  },
  {
    id: 'boards',
    label: 'Boards',
    icon: 'B',
    path: '/workspace/boards',
    flag: null,
    primaryModes: ['guided', 'workbench', 'agent'],
    keywords: 'boards projects workspace',
  },
  {
    id: 'inbox',
    label: 'Inbox',
    icon: 'I',
    path: '/workspace/inbox',
    flag: null,
    primaryModes: ['guided', 'workbench', 'agent'],
    keywords: 'inbox captures triage',
  },
  {
    id: 'notifications',
    label: 'Notifications',
    icon: 'N',
    path: '/workspace/notifications',
    flag: null,
    primaryModes: ['workbench'],
    secondaryModes: ['guided', 'agent'],
    keywords: 'notifications updates mention assignment',
  },
  {
    id: 'chat',
    label: 'Chat',
    icon: 'C',
    path: '/workspace/automations/chat',
    flag: 'newAutomation',
    primaryModes: ['workbench'],
    secondaryModes: ['guided', 'agent'],
    keywords: 'chat automation assistant board context',
  },
  {
    id: 'activity',
    label: 'Activity',
    icon: 'T',
    path: '/workspace/activity',
    flag: 'newActivity',
    primaryModes: ['workbench'],
    secondaryModes: ['guided', 'agent'],
    keywords: 'activity audit history events',
  },
  {
    id: 'ops',
    label: 'Ops',
    icon: 'O',
    path: '/workspace/ops/cli',
    flag: 'newOps',
    primaryModes: ['workbench'],
    secondaryModes: ['guided', 'agent'],
    keywords: 'ops logs cli endpoints',
  },
  {
    id: 'settings',
    label: 'Settings',
    icon: 'S',
    path: '/workspace/settings/profile',
    flag: 'newAuth',
    primaryModes: ['workbench'],
    secondaryModes: ['guided', 'agent'],
    keywords: 'settings profile password account',
  },
  {
    id: 'preferences',
    label: 'Preferences',
    icon: 'P',
    path: '/workspace/settings/preferences',
    flag: null,
    primaryModes: ['workbench'],
    secondaryModes: ['guided', 'agent'],
    keywords: 'preferences notifications',
  },
  {
    id: 'access',
    label: 'Access',
    icon: 'A',
    path: '/workspace/settings/access',
    flag: 'newAccess',
    primaryModes: ['workbench'],
    secondaryModes: ['guided', 'agent'],
    keywords: 'access board sharing permissions',
  },
  {
    id: 'archive',
    label: 'Archive',
    icon: 'Z',
    path: '/workspace/archive',
    flag: 'newArchive',
    primaryModes: ['workbench'],
    secondaryModes: ['guided', 'agent'],
    keywords: 'archive restore hidden boards',
  },
]

const availableNavItems = computed(() => navCatalog.filter((item) => {
  if (!item.flag) return true
  return featureFlags.isEnabled(item.flag as keyof typeof featureFlags.flags)
}))

const activeWorkspaceMode = computed<WorkspaceMode>(() =>
  isWorkspaceMode(workspace.mode)
    ? workspace.mode
    : 'guided')

const primaryNavItems = computed(() => availableNavItems.value.filter((item) => item.primaryModes.includes(activeWorkspaceMode.value)))

const secondaryNavItems = computed(() => availableNavItems.value.filter((item) => item.secondaryModes?.includes(activeWorkspaceMode.value)))

const currentModeMeta = computed(() => workspaceModeMeta[activeWorkspaceMode.value])

function openCaptureModal() {
  showCaptureModal.value = true
}

function closeCaptureModal() {
  showCaptureModal.value = false
}

function handleCaptureCreated() {
  closeCaptureModal()
  void router.push('/workspace/inbox')
}

const commandItems = computed<CommandItem[]>(() => {
  const navigationItems = availableNavItems.value.map((item) => ({
    id: `nav:${item.path}`,
    label: item.label,
    icon: item.icon,
    path: item.path,
    keywords: `${item.path} ${item.keywords ?? ''}`.trim(),
    kind: 'navigation' as const,
  }))

  return [
    ...navigationItems,
    {
      id: 'action:capture',
      label: 'New Capture',
      icon: '+',
      keywords: 'capture inbox quick note modal',
      kind: 'action',
      action: openCaptureModal,
    },
  ]
})

const filteredCommandItems = computed(() => {
  const normalizedQuery = commandQuery.value.trim().toLowerCase()
  if (!normalizedQuery) {
    return commandItems.value
  }

  return commandItems.value.filter((item) =>
    item.label.toLowerCase().includes(normalizedQuery) ||
    item.path?.toLowerCase().includes(normalizedQuery) ||
    item.keywords?.toLowerCase().includes(normalizedQuery)
  )
})

const activeCommandId = computed(() => {
  if (filteredCommandItems.value.length === 0) {
    return undefined
  }

  return `td-command-option-${selectedCommandIndex.value}`
})

function isActiveRoute(path: string): boolean {
  if (path === '/workspace/home') {
    return route.path === path
  }

  if (path.startsWith('/workspace/automations')) {
    return route.path.startsWith('/workspace/automations')
  }

  if (path === '/workspace/ops/cli') {
    return route.path.startsWith('/workspace/ops')
  }

  return route.path.startsWith(path)
}

function toggleSidebar() {
  sidebarCollapsed.value = !sidebarCollapsed.value
}

function handleLogout() {
  session.logout()
  void router.push('/login')
}

function handleWorkspaceModeChange(event: Event) {
  const nextMode = (event.target as HTMLSelectElement | null)?.value
  if (!nextMode || !isWorkspaceMode(nextMode)) {
    return
  }

  void workspace.updateMode(nextMode)
}

function openCommandPalette() {
  showCommandPalette.value = true
  commandQuery.value = ''
  selectedCommandIndex.value = 0
}

function closeCommandPalette() {
  showCommandPalette.value = false
  commandQuery.value = ''
  selectedCommandIndex.value = 0
}

function selectNextCommand() {
  const itemCount = filteredCommandItems.value.length
  if (itemCount === 0) return
  selectedCommandIndex.value = (selectedCommandIndex.value + 1) % itemCount
}

function selectPreviousCommand() {
  const itemCount = filteredCommandItems.value.length
  if (itemCount === 0) return
  selectedCommandIndex.value = (selectedCommandIndex.value - 1 + itemCount) % itemCount
}

function setSelectedCommand(index: number) {
  selectedCommandIndex.value = index
}

function activateCommand(item: CommandItem) {
  if (item.action) {
    item.action()
    closeCommandPalette()
    return
  }

  if (item.path) {
    void router.push(item.path)
  }

  closeCommandPalette()
}

function activateSelectedCommand() {
  const selectedItem = filteredCommandItems.value[selectedCommandIndex.value]
  if (!selectedItem) return
  activateCommand(selectedItem)
}

function isTextEntryTarget(target: EventTarget | null): boolean {
  return target instanceof HTMLInputElement ||
    target instanceof HTMLTextAreaElement
}

function handleKeydown(event: KeyboardEvent) {
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
    event.preventDefault()
    if (showCommandPalette.value) {
      closeCommandPalette()
      return
    }
    openCommandPalette()
  }

  if ((event.ctrlKey || event.metaKey) && event.shiftKey && event.key.toLowerCase() === 'c' && !isTextEntryTarget(event.target)) {
    event.preventDefault()
    openCaptureModal()
  }

  if (event.key === '?' && !isTextEntryTarget(event.target)) {
    showKeyboardHelp.value = !showKeyboardHelp.value
  }
}

watch(showCommandPalette, async (isOpen) => {
  if (!isOpen) return
  await nextTick()
  commandPaletteInput.value?.focus()
})

watch(showCommandPalette, (isOpen, _, onCleanup) => {
  if (!isOpen) return
  const unregisterEscapeHandler = registerEscapeHandler(closeCommandPalette)
  onCleanup(() => {
    unregisterEscapeHandler()
  })
})

watch(showKeyboardHelp, (isOpen, _, onCleanup) => {
  if (!isOpen) return
  const unregisterEscapeHandler = registerEscapeHandler(() => {
    showKeyboardHelp.value = false
  })
  onCleanup(() => {
    unregisterEscapeHandler()
  })
})

watch(filteredCommandItems, (items) => {
  if (items.length === 0) {
    selectedCommandIndex.value = 0
    return
  }

  if (selectedCommandIndex.value >= items.length) {
    selectedCommandIndex.value = 0
  }
})

onMounted(() => {
  window.addEventListener('keydown', handleKeydown)
})

onUnmounted(() => {
  window.removeEventListener('keydown', handleKeydown)
})
</script>

<template>
  <div class="td-shell">
    <aside
      class="td-sidebar"
      :class="{ 'td-sidebar--collapsed': sidebarCollapsed }"
      role="navigation"
      aria-label="Main navigation"
    >
      <div class="td-sidebar__header">
        <h1 v-if="!sidebarCollapsed" class="td-sidebar__title">Taskdeck</h1>
        <button
          class="td-sidebar__toggle"
          :aria-label="sidebarCollapsed ? 'Expand sidebar' : 'Collapse sidebar'"
          @click="toggleSidebar"
        >
          {{ sidebarCollapsed ? '>' : '<' }}
        </button>
      </div>

      <nav class="td-sidebar__nav">
        <router-link
          v-for="item in primaryNavItems"
          :key="item.id"
          :to="item.path"
          class="td-nav-item"
          :class="{ 'td-nav-item--active': isActiveRoute(item.path) }"
          :aria-current="isActiveRoute(item.path) ? 'page' : undefined"
        >
          <span class="td-nav-item__icon">{{ item.icon }}</span>
          <span v-if="!sidebarCollapsed" class="td-nav-item__label">{{ item.label }}</span>
        </router-link>

        <div v-if="secondaryNavItems.length > 0" class="td-sidebar__section">
          <div v-if="!sidebarCollapsed" class="td-sidebar__section-label">Workbench Tools</div>
          <router-link
            v-for="item in secondaryNavItems"
            :key="item.id"
            :to="item.path"
            class="td-nav-item td-nav-item--secondary"
            :class="{ 'td-nav-item--active': isActiveRoute(item.path) }"
            :aria-current="isActiveRoute(item.path) ? 'page' : undefined"
          >
            <span class="td-nav-item__icon">{{ item.icon }}</span>
            <span v-if="!sidebarCollapsed" class="td-nav-item__label">{{ item.label }}</span>
          </router-link>
        </div>
      </nav>

      <div class="td-sidebar__footer">
        <button
          v-if="!sidebarCollapsed"
          class="td-nav-item td-nav-item--help"
          aria-label="Keyboard shortcuts help"
          @click="showKeyboardHelp = true"
        >
          <span class="td-nav-item__icon">?</span>
          <span class="td-nav-item__label">Shortcuts (?)</span>
        </button>
      </div>
    </aside>

    <div class="td-main-container">
      <header class="td-topbar" role="banner">
        <div class="td-topbar__left">
          <div class="td-topbar__mode">
            <label class="td-topbar__mode-label" for="workspace-mode-select">Workspace mode</label>
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
            @click="openCommandPalette"
          >
            <span class="td-topbar__search-icon">/</span>
            <span class="td-topbar__search-text">Go anywhere or run a command... (Ctrl+K)</span>
          </button>
        </div>

        <div class="td-topbar__right">
          <span v-if="session.isAuthenticated" class="td-topbar__user">
            {{ session.username }}
          </span>
          <button
            v-if="session.isAuthenticated"
            class="td-topbar__logout"
            aria-label="Log out"
            @click="handleLogout"
          >
            Logout
          </button>
        </div>
      </header>

      <main class="td-content" role="main">
        <router-view />
      </main>
    </div>

    <Teleport to="body">
      <div
        v-if="showCommandPalette"
        class="td-overlay"
        role="dialog"
        aria-label="Command palette"
        aria-modal="true"
        @click.self="closeCommandPalette"
      >
        <div class="td-command-palette">
          <input
            ref="commandPaletteInput"
            v-model="commandQuery"
            type="text"
            class="td-command-palette__input"
            placeholder="Type a command or search..."
            autofocus
            role="combobox"
            aria-autocomplete="list"
            :aria-expanded="showCommandPalette"
            :aria-controls="commandListboxId"
            :aria-activedescendant="activeCommandId"
            @keydown.escape.prevent="closeCommandPalette"
            @keydown.down.prevent="selectNextCommand"
            @keydown.up.prevent="selectPreviousCommand"
            @keydown.enter.prevent="activateSelectedCommand"
          />
          <div
            :id="commandListboxId"
            class="td-command-palette__results"
            role="listbox"
            aria-label="Commands"
          >
            <div class="td-command-palette__group">
              <div class="td-command-palette__group-title">Commands</div>
              <div
                v-for="(item, index) in filteredCommandItems"
                :key="item.id"
                :id="`td-command-option-${index}`"
                :data-command-index="index"
                :class="[
                  'td-command-palette__item',
                  index === selectedCommandIndex ? 'td-command-palette__item--active' : ''
                ]"
                role="option"
                :aria-selected="index === selectedCommandIndex"
                @mouseenter="setSelectedCommand(index)"
                @click="activateCommand(item)"
              >
                <span>{{ item.icon }}</span>
                <span>{{ item.kind === 'navigation' ? `Go to ${item.label}` : item.label }}</span>
              </div>
              <div v-if="filteredCommandItems.length === 0" class="td-command-palette__empty">
                No matching commands.
              </div>
            </div>
          </div>
        </div>
      </div>
    </Teleport>

    <Teleport to="body">
      <div
        v-if="showKeyboardHelp"
        class="td-overlay"
        role="dialog"
        aria-label="Keyboard shortcuts"
        aria-modal="true"
        @click.self="showKeyboardHelp = false"
      >
        <div class="td-keyboard-help">
          <div class="td-keyboard-help__header">
            <h2>Keyboard Shortcuts</h2>
            <button aria-label="Close" @click="showKeyboardHelp = false">X</button>
          </div>
          <div class="td-keyboard-help__content">
            <div class="td-keyboard-help__section">
              <h3>Global</h3>
              <div class="td-shortcut-row"><kbd>Ctrl+K</kbd><span>Command palette</span></div>
              <div class="td-shortcut-row"><kbd>Ctrl+Shift+C</kbd><span>Quick capture modal</span></div>
              <div class="td-shortcut-row"><kbd>?</kbd><span>This help</span></div>
              <div class="td-shortcut-row"><kbd>Escape</kbd><span>Close top surface</span></div>
            </div>
            <div class="td-keyboard-help__section">
              <h3>Board Navigation</h3>
              <div class="td-shortcut-row"><kbd>h / Left</kbd><span>Previous column</span></div>
              <div class="td-shortcut-row"><kbd>l / Right</kbd><span>Next column</span></div>
              <div class="td-shortcut-row"><kbd>j / Down</kbd><span>Next card</span></div>
              <div class="td-shortcut-row"><kbd>k / Up</kbd><span>Previous card</span></div>
              <div class="td-shortcut-row"><kbd>Enter</kbd><span>Open card</span></div>
              <div class="td-shortcut-row"><kbd>n</kbd><span>New card</span></div>
              <div class="td-shortcut-row"><kbd>Shift+N</kbd><span>New column</span></div>
            </div>
            <div class="td-keyboard-help__section">
              <h3>Editor</h3>
              <div class="td-shortcut-row"><kbd>Ctrl+S</kbd><span>Save section</span></div>
              <div class="td-shortcut-row"><kbd>Ctrl+Enter</kbd><span>Save and close</span></div>
              <div class="td-shortcut-row"><kbd>Alt+1</kbd><span>Jump to title</span></div>
              <div class="td-shortcut-row"><kbd>Alt+2</kbd><span>Jump to description</span></div>
              <div class="td-shortcut-row"><kbd>Alt+4</kbd><span>Jump to labels</span></div>
            </div>
          </div>
        </div>
      </div>
    </Teleport>

    <Teleport to="body">
      <CaptureModal
        v-if="showCaptureModal"
        @close="closeCaptureModal"
        @created="handleCaptureCreated"
      />
    </Teleport>
  </div>
</template>

<style scoped>
.td-shell {
  display: flex;
  min-height: 100vh;
  background: var(--td-surface-secondary);
}

.td-sidebar {
  width: var(--td-sidebar-width);
  background: var(--td-text-primary);
  color: var(--td-text-inverse);
  display: flex;
  flex-direction: column;
  transition: width var(--td-transition-normal);
  flex-shrink: 0;
}

.td-sidebar--collapsed {
  width: var(--td-sidebar-collapsed-width);
}

.td-sidebar__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: var(--td-space-4);
  border-bottom: 1px solid rgba(255, 255, 255, 0.1);
}

.td-sidebar__title {
  font-size: var(--td-font-lg);
  font-weight: 700;
  white-space: nowrap;
}

.td-sidebar__toggle {
  background: transparent;
  border: none;
  color: var(--td-text-inverse);
  cursor: pointer;
  padding: var(--td-space-2);
  border-radius: var(--td-radius-md);
}

.td-sidebar__toggle:hover {
  background: rgba(255, 255, 255, 0.1);
}

.td-sidebar__nav {
  flex: 1;
  padding: var(--td-space-2);
  display: flex;
  flex-direction: column;
  gap: var(--td-space-1);
}

.td-sidebar__section {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-1);
  margin-top: var(--td-space-3);
}

.td-sidebar__section-label {
  padding: var(--td-space-2) var(--td-space-3);
  font-size: var(--td-font-xs);
  font-weight: 700;
  color: rgba(255, 255, 255, 0.55);
  letter-spacing: 0.08em;
  text-transform: uppercase;
}

.td-sidebar__footer {
  padding: var(--td-space-2);
  border-top: 1px solid rgba(255, 255, 255, 0.1);
}

.td-nav-item {
  display: flex;
  align-items: center;
  gap: var(--td-space-3);
  padding: var(--td-space-2) var(--td-space-3);
  border-radius: var(--td-radius-md);
  color: rgba(255, 255, 255, 0.7);
  text-decoration: none;
  transition: all var(--td-transition-fast);
  cursor: pointer;
  border: none;
  background: transparent;
  width: 100%;
  text-align: left;
  font-size: var(--td-font-sm);
}

.td-nav-item:hover {
  background: rgba(255, 255, 255, 0.1);
  color: var(--td-text-inverse);
}

.td-nav-item--active {
  background: rgba(255, 255, 255, 0.15);
  color: var(--td-text-inverse);
  font-weight: 600;
}

.td-nav-item--secondary {
  color: rgba(255, 255, 255, 0.62);
}

.td-nav-item__icon {
  font-size: var(--td-font-lg);
  flex-shrink: 0;
}

.td-nav-item__label {
  white-space: nowrap;
}

.td-main-container {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.td-topbar {
  background: var(--td-surface-primary);
  border-bottom: 1px solid var(--td-border-default);
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: var(--td-space-3) var(--td-space-4);
  flex-shrink: 0;
  gap: var(--td-space-3);
}

.td-topbar__left {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: var(--td-space-2);
}

.td-topbar__mode {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.td-topbar__mode-label {
  font-size: var(--td-font-xs);
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  color: var(--td-text-tertiary);
}

.td-topbar__mode-controls {
  display: flex;
  align-items: center;
  gap: var(--td-space-3);
  flex-wrap: wrap;
}

.td-topbar__mode-select {
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-md);
  background: var(--td-surface-primary);
  color: var(--td-text-primary);
  padding: 0.35rem 0.75rem;
  font-size: var(--td-font-sm);
  min-width: 140px;
}

.td-topbar__mode-copy {
  font-size: var(--td-font-sm);
  color: var(--td-text-secondary);
}

.td-topbar__palette-trigger {
  display: flex;
  align-items: center;
  gap: var(--td-space-2);
  padding: var(--td-space-1) var(--td-space-3);
  background: var(--td-surface-tertiary);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-lg);
  color: var(--td-text-tertiary);
  cursor: pointer;
  font-size: var(--td-font-sm);
  min-width: 280px;
  width: fit-content;
}

.td-topbar__palette-trigger:hover {
  border-color: var(--td-border-focus);
}

.td-topbar__right {
  display: flex;
  align-items: center;
  gap: var(--td-space-3);
}

.td-topbar__user {
  font-size: var(--td-font-sm);
  color: var(--td-text-secondary);
  font-weight: 500;
}

.td-topbar__logout {
  font-size: var(--td-font-sm);
  color: var(--td-text-secondary);
  background: transparent;
  border: 1px solid var(--td-border-default);
  padding: var(--td-space-1) var(--td-space-3);
  border-radius: var(--td-radius-md);
  cursor: pointer;
}

.td-topbar__logout:hover {
  background: var(--td-surface-hover);
}

.td-content {
  flex: 1;
  overflow-y: auto;
  padding: var(--td-space-6);
}

.td-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: flex-start;
  justify-content: center;
  padding-top: 15vh;
  z-index: 50;
}

.td-command-palette {
  background: var(--td-surface-primary);
  border-radius: var(--td-radius-xl);
  box-shadow: var(--td-shadow-xl);
  width: 100%;
  max-width: 560px;
  overflow: hidden;
}

.td-command-palette__input {
  width: 100%;
  padding: var(--td-space-4);
  border: none;
  font-size: var(--td-font-lg);
  outline: none;
  border-bottom: 1px solid var(--td-border-default);
}

.td-command-palette__results {
  max-height: 300px;
  overflow-y: auto;
  padding: var(--td-space-2);
}

.td-command-palette__group-title {
  font-size: var(--td-font-xs);
  font-weight: 600;
  color: var(--td-text-tertiary);
  text-transform: uppercase;
  padding: var(--td-space-2) var(--td-space-3);
}

.td-command-palette__item {
  display: flex;
  align-items: center;
  gap: var(--td-space-3);
  width: 100%;
  padding: var(--td-space-2) var(--td-space-3);
  border: none;
  background: transparent;
  cursor: pointer;
  border-radius: var(--td-radius-md);
  font-size: var(--td-font-sm);
  text-align: left;
}

.td-command-palette__item:hover {
  background: var(--td-surface-tertiary);
}

.td-command-palette__item--active {
  background: var(--td-surface-tertiary);
  outline: 1px solid var(--td-border-focus);
}

.td-command-palette__empty {
  padding: var(--td-space-3);
  color: var(--td-text-tertiary);
  font-size: var(--td-font-sm);
}

.td-keyboard-help {
  background: var(--td-surface-primary);
  border-radius: var(--td-radius-xl);
  box-shadow: var(--td-shadow-xl);
  width: 100%;
  max-width: 500px;
  max-height: 80vh;
  overflow-y: auto;
}

.td-keyboard-help__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: var(--td-space-4) var(--td-space-6);
  border-bottom: 1px solid var(--td-border-default);
}

.td-keyboard-help__header h2 {
  font-size: var(--td-font-lg);
  font-weight: 700;
}

.td-keyboard-help__header button {
  background: transparent;
  border: none;
  font-size: var(--td-font-lg);
  cursor: pointer;
  color: var(--td-text-secondary);
}

.td-keyboard-help__content {
  padding: var(--td-space-4) var(--td-space-6);
}

.td-keyboard-help__section {
  margin-bottom: var(--td-space-6);
}

.td-keyboard-help__section h3 {
  font-size: var(--td-font-sm);
  font-weight: 600;
  color: var(--td-text-secondary);
  text-transform: uppercase;
  margin-bottom: var(--td-space-2);
}

.td-shortcut-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: var(--td-space-1) 0;
  font-size: var(--td-font-sm);
}

.td-shortcut-row kbd {
  background: var(--td-surface-tertiary);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-sm);
  padding: 1px 6px;
  font-family: monospace;
  font-size: var(--td-font-xs);
}

@media (max-width: 900px) {
  .td-topbar {
    flex-direction: column;
    align-items: stretch;
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
