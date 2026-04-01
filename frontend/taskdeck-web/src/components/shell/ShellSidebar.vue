<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { registerEscapeHandler } from '../../composables/useEscapeStack'
import { useFeatureFlagStore } from '../../store/featureFlagStore'
import { useWorkspaceStore } from '../../store/workspaceStore'
import type { FeatureFlags } from '../../types/feature-flags'
import type { WorkspaceMode } from '../../types/workspace'
import { isWorkspaceMode } from '../../types/workspace'

export type NavItem = {
  id: string
  label: string
  icon: string
  path: string
  flag: keyof FeatureFlags | null
  workbenchBypassesFlag?: boolean
  primaryModes: WorkspaceMode[]
  secondaryModes?: WorkspaceMode[]
  keywords?: string
}

defineProps<{
  isAuthenticated: boolean
}>()

const emit = defineEmits<{
  logout: []
  'show-keyboard-help': []
}>()

const route = useRoute()
const featureFlags = useFeatureFlagStore()
const workspace = useWorkspaceStore()

const sidebarCollapsed = ref(false)
const mobileOpen = ref(false)

function closeMobileMenu() {
  mobileOpen.value = false
}

function toggleMobileMenu() {
  mobileOpen.value = !mobileOpen.value
}

// Lock body scroll and register Escape handler while mobile menu is open
watch(mobileOpen, (isOpen, _, onCleanup) => {
  if (!isOpen) return

  document.body.style.overflow = 'hidden'
  const unregisterEscape = registerEscapeHandler(closeMobileMenu)

  onCleanup(() => {
    document.body.style.overflow = ''
    unregisterEscape()
  })
})

onUnmounted(() => {
  // Safety: restore scroll if component unmounts while open
  if (mobileOpen.value) {
    document.body.style.overflow = ''
  }
})

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
    id: 'today',
    label: 'Today',
    icon: 'T',
    path: '/workspace/today',
    flag: null,
    primaryModes: ['guided', 'workbench', 'agent'],
    keywords: 'today agenda daily focus overdue blocked',
  },
  {
    id: 'review',
    label: 'Review',
    icon: 'R',
    path: '/workspace/review',
    flag: 'newAutomation',
    workbenchBypassesFlag: true,
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
    id: 'views',
    label: 'Views',
    icon: 'V',
    path: '/workspace/views',
    flag: null,
    primaryModes: ['workbench'],
    secondaryModes: ['guided', 'agent'],
    keywords: 'views saved filters shortcuts blocked due week review',
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
    workbenchBypassesFlag: true,
    primaryModes: ['workbench'],
    secondaryModes: ['guided', 'agent'],
    keywords: 'chat automation assistant board context',
  },
  {
    id: 'metrics',
    label: 'Metrics',
    icon: 'M',
    path: '/workspace/metrics',
    flag: null,
    primaryModes: ['workbench'],
    secondaryModes: ['guided', 'agent'],
    keywords: 'metrics analytics throughput cycle time wip blocked dashboard',
  },
  {
    id: 'activity',
    label: 'Activity',
    icon: 'Y',
    path: '/workspace/activity',
    flag: 'newActivity',
    workbenchBypassesFlag: true,
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
    workbenchBypassesFlag: true,
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
    workbenchBypassesFlag: true,
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
    workbenchBypassesFlag: true,
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
    workbenchBypassesFlag: true,
    primaryModes: ['workbench'],
    secondaryModes: ['guided', 'agent'],
    keywords: 'archive restore hidden boards',
  },
]

const activeWorkspaceMode = computed<WorkspaceMode>(() =>
  isWorkspaceMode(workspace.mode)
    ? workspace.mode
    : 'guided')

const availableNavItems = computed(() => navCatalog.filter((item) => {
  if (!item.flag) return true
  if (activeWorkspaceMode.value === 'workbench' && item.workbenchBypassesFlag) return true
  return featureFlags.isEnabled(item.flag)
}))

const primaryNavItems = computed(() => availableNavItems.value.filter((item) => item.primaryModes.includes(activeWorkspaceMode.value)))

const secondaryNavItems = computed(() => availableNavItems.value.filter((item) => item.secondaryModes?.includes(activeWorkspaceMode.value)))

const navBadgeCounts = computed<Record<string, number>>(() => ({
  '/workspace/inbox': workspace.inboxBadgeCount,
  '/workspace/review': workspace.reviewBadgeCount,
}))

function isActiveRoute(path: string): boolean {
  if (path === '/workspace/home') {
    return route.path === path
  }

  if (path === '/workspace/review') {
    return route.path.startsWith('/workspace/review')
      || route.path.startsWith('/workspace/automations/proposals')
      || route.path.startsWith('/workspace/automations/queue')
  }

  if (path.startsWith('/workspace/automations')) {
    return route.path.startsWith(path)
  }

  if (path === '/workspace/ops/cli') {
    return route.path.startsWith('/workspace/ops')
  }

  return route.path.startsWith(path)
}

function toggleSidebar() {
  sidebarCollapsed.value = !sidebarCollapsed.value
}

/**
 * Expose available nav items so the parent orchestrator can build
 * command palette items from them.
 */
defineExpose({
  availableNavItems,
  mobileOpen,
  toggleMobileMenu,
  closeMobileMenu,
})
</script>

<template>
  <div
    v-if="mobileOpen"
    class="td-sidebar-overlay"
    aria-hidden="true"
    @click="closeMobileMenu"
  />
  <aside
    class="td-sidebar"
    :class="{ 'td-sidebar--collapsed': sidebarCollapsed, 'td-sidebar--mobile-open': mobileOpen }"
    role="navigation"
    aria-label="Main navigation"
  >
    <div class="td-sidebar__header">
      <div v-if="!sidebarCollapsed" class="td-sidebar__brand">
        <span class="td-sidebar__title">Taskdeck</span>
        <span
          class="td-sidebar__subtitle"
          title="Precision Mode: the workspace operates with guided automation — all proposals require explicit review before applying to the board. Change this in Preferences."
        >Precision Mode Active</span>
      </div>
      <button
        class="td-sidebar__toggle"
        :aria-label="sidebarCollapsed ? 'Expand sidebar' : 'Collapse sidebar'"
        @click="toggleSidebar"
      >
        <span class="material-symbols-outlined">{{ sidebarCollapsed ? 'chevron_right' : 'chevron_left' }}</span>
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
        @click="closeMobileMenu"
      >
        <span class="td-nav-item__icon">{{ item.icon }}</span>
        <span v-if="!sidebarCollapsed" class="td-nav-item__label">{{ item.label }}</span>
        <span
          v-if="(navBadgeCounts[item.path] ?? 0) > 0"
          class="td-nav-badge"
          :aria-label="`${item.label}: ${navBadgeCounts[item.path]} pending`"
        >{{ navBadgeCounts[item.path] }}</span>
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
          @click="closeMobileMenu"
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
        @click="emit('show-keyboard-help')"
      >
        <span class="td-nav-item__icon">?</span>
        <span class="td-nav-item__label">Shortcuts</span>
      </button>
      <button
        v-if="isAuthenticated"
        class="td-nav-item td-nav-item--logout"
        aria-label="Log out"
        @click="emit('logout')"
      >
        <span class="td-nav-item__icon">
          <span class="material-symbols-outlined text-base">logout</span>
        </span>
        <span v-if="!sidebarCollapsed" class="td-nav-item__label">Logout</span>
      </button>
    </div>
  </aside>
</template>

<style scoped>
.td-sidebar {
  width: var(--td-sidebar-width);
  position: sticky;
  top: 0;
  height: 100vh;
  height: 100dvh;
  background: var(--td-surface-container);
  color: var(--td-text-primary);
  display: flex;
  flex-direction: column;
  transition: width var(--td-transition-smooth);
  flex-shrink: 0;
  box-shadow: var(--td-shadow-lg);
  z-index: 40;
}

.td-sidebar--collapsed {
  width: var(--td-sidebar-collapsed-width);
}

.td-sidebar__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: var(--td-space-5);
  min-height: var(--td-topbar-height);
}

.td-sidebar__brand {
  display: flex;
  flex-direction: column;
  gap: var(--td-space-1);
}

.td-sidebar__title {
  font-family: 'Manrope', system-ui, sans-serif;
  font-size: var(--td-font-xl);
  font-weight: 800;
  letter-spacing: -0.04em;
  color: var(--td-text-primary);
  white-space: nowrap;
}

.td-sidebar__subtitle {
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: var(--td-font-xs);
  font-weight: 700;
  letter-spacing: 0.15em;
  text-transform: uppercase;
  color: var(--td-color-ember);
}

.td-sidebar__toggle {
  background: transparent;
  border: none;
  color: var(--td-text-tertiary);
  cursor: pointer;
  padding: var(--td-space-2);
  border-radius: var(--td-radius-md);
  transition: color var(--td-transition-fast), background var(--td-transition-fast);
}

.td-sidebar__toggle:hover {
  color: var(--td-color-ember);
  background: var(--td-surface-container-high);
}

.td-sidebar__toggle:focus-visible {
  box-shadow: var(--td-focus-ring);
  outline: none;
}

.td-sidebar__nav {
  flex: 1;
  overflow-y: auto;
  padding-top: var(--td-space-5);
  display: flex;
  flex-direction: column;
  gap: 1px;
}

.td-sidebar__section {
  display: flex;
  flex-direction: column;
  gap: 1px;
  margin-top: var(--td-space-5);
}

.td-sidebar__section-label {
  padding: var(--td-space-4) var(--td-space-5);
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: var(--td-font-xs);
  font-weight: 700;
  color: var(--td-text-tertiary);
  letter-spacing: 0.2em;
  text-transform: uppercase;
}

.td-nav-item {
  display: flex;
  align-items: center;
  gap: var(--td-space-4);
  padding: var(--td-space-4) var(--td-space-5);
  color: var(--td-text-tertiary);
  text-decoration: none;
  transition: all var(--td-transition-normal);
  cursor: pointer;
  border: none;
  background: transparent;
  width: 100%;
  text-align: left;
  font-family: 'Space Grotesk', system-ui, sans-serif;
  font-size: var(--td-font-xs);
  font-weight: 400;
  letter-spacing: 0.15em;
  text-transform: uppercase;
  position: relative;
}

.td-nav-item:hover {
  background: var(--td-surface-bright);
  color: var(--td-text-primary);
}

.td-nav-item:active {
  transform: translateX(2px);
}

.td-nav-item:focus-visible {
  box-shadow: var(--td-focus-ring);
  outline: none;
}

.td-nav-item--active {
  background: linear-gradient(to right, var(--td-color-ember-dim), transparent);
  border-left: 4px solid var(--td-color-ember);
  color: var(--td-color-ember);
  font-weight: 700;
}

.td-nav-item--active .td-nav-item__icon {
  color: var(--td-color-ember);
}

.td-nav-item--secondary {
  color: var(--td-text-tertiary);
}

.td-nav-item--logout {
  color: var(--td-text-tertiary);
}

.td-nav-item--logout:hover {
  color: var(--td-color-primary);
}

.td-nav-item__icon {
  font-size: var(--td-font-base);
  flex-shrink: 0;
  width: 20px;
  text-align: center;
  font-weight: 700;
}

.td-nav-item__label {
  white-space: nowrap;
}

.td-nav-badge {
  margin-left: auto;
  min-width: 18px;
  height: 18px;
  padding: 0 var(--td-space-2);
  border-radius: 9999px;
  background: var(--td-color-ember);
  color: var(--td-text-inverse);
  font-size: var(--td-font-xs);
  font-weight: 700;
  line-height: 18px;
  text-align: center;
  flex-shrink: 0;
}

.td-sidebar__footer {
  flex-shrink: 0;
  padding: var(--td-space-3);
  border-top: 1px solid var(--td-border-ghost);
  display: flex;
  flex-direction: column;
  gap: 1px;
}

/* ─── Mobile overlay ─── */
.td-sidebar-overlay {
  display: none;
}

/* ─── Mobile: sidebar off-canvas ─── */
@media (max-width: 640px) {
  .td-sidebar {
    position: fixed;
    top: 0;
    left: 0;
    bottom: 0;
    height: auto;
    transform: translateX(-100%);
    transition: transform 0.25s ease;
    z-index: 50;
  }

  .td-sidebar--mobile-open {
    transform: translateX(0);
  }

  .td-sidebar-overlay {
    display: block;
    position: fixed;
    inset: 0;
    background: rgba(0, 0, 0, 0.5);
    z-index: 45;
  }

  .td-nav-item {
    min-height: 44px;
  }
}
</style>
