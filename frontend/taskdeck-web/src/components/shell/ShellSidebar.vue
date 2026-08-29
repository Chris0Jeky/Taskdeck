<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { registerEscapeHandler } from '../../composables/useEscapeStack'
import { orderGuidedAdvancedDestinations } from '../guidedAdvancedNavigation'
import { useFeatureFlagStore } from '../../store/featureFlagStore'
import { useWorkspaceStore } from '../../store/workspaceStore'
import type { FeatureFlags } from '../../types/feature-flags'
import type { WorkspaceMode } from '../../types/workspace'
import { isWorkspaceMode } from '../../types/workspace'
import { formatShortcut } from '../../utils/keyboardShortcuts'

// Rendered notation follows the viewer's platform (Command glyph on Apple),
// so the palette hint is never a hardcoded Ctrl literal.
const commandPaletteKeys = formatShortcut('mod+k')

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
  /** When true, this item appears in the reduced primary sidebar IA. */
  sidebarPrimary?: boolean
  /** Developer-facing destination hidden behind guided mode's Advanced disclosure. */
  guidedAdvanced?: boolean
}

defineProps<{
  isAuthenticated: boolean
}>()

const emit = defineEmits<{
  logout: []
  'show-keyboard-help': []
  'open-search': []
}>()

const route = useRoute()
const featureFlags = useFeatureFlagStore()
const workspace = useWorkspaceStore()

const sidebarCollapsed = ref(false)
const mobileOpen = ref(false)
const guidedAdvancedRevealed = ref(false)

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

/**
 * Reduced IA model (v4 roadmap):
 * Primary sidebar shows only: Today, Inbox, Review, Boards, Search.
 * All other surfaces remain routable through the command palette (Ctrl+K)
 * and from the Settings page. Items with sidebarPrimary: true appear in
 * the sidebar; the rest are command-palette-only.
 */
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
    sidebarPrimary: true,
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
    sidebarPrimary: true,
    keywords: 'review proposals automations approve reject execute',
  },
  {
    id: 'boards',
    label: 'Boards',
    icon: 'B',
    path: '/workspace/boards',
    flag: null,
    primaryModes: ['guided', 'workbench', 'agent'],
    sidebarPrimary: true,
    keywords: 'boards projects workspace',
  },
  {
    id: 'inbox',
    label: 'Inbox',
    icon: 'I',
    path: '/workspace/inbox',
    flag: null,
    primaryModes: ['guided', 'workbench', 'agent'],
    sidebarPrimary: true,
    keywords: 'inbox captures triage',
  },
  {
    id: 'agents',
    label: 'Agents',
    icon: 'G',
    path: '/workspace/agents',
    flag: null,
    primaryModes: ['agent'],
    guidedAdvanced: true,
    keywords: 'agents profiles runs automation agent mode',
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
    id: 'calendar',
    label: 'Calendar',
    icon: 'D',
    path: '/workspace/calendar',
    flag: null,
    primaryModes: ['workbench'],
    secondaryModes: ['guided', 'agent'],
    keywords: 'calendar timeline planning due dates schedule deadlines',
  },
  {
    id: 'metrics',
    label: 'Metrics',
    icon: 'M',
    path: '/workspace/metrics',
    flag: null,
    primaryModes: ['workbench'],
    secondaryModes: ['guided', 'agent'],
    guidedAdvanced: true,
    keywords: 'metrics analytics throughput cycle time wip blocked dashboard',
  },
  {
    id: 'integrations',
    label: 'Integrations',
    icon: 'X',
    path: '/workspace/integrations',
    flag: null,
    primaryModes: ['workbench'],
    secondaryModes: ['guided', 'agent'],
    guidedAdvanced: true,
    keywords: 'integrations connectors inbound outbound webhook import',
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
    guidedAdvanced: true,
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
    id: 'api-keys',
    label: 'API Keys',
    icon: 'K',
    path: '/workspace/settings/api-keys',
    flag: null,
    primaryModes: ['workbench'],
    secondaryModes: ['guided', 'agent'],
    guidedAdvanced: true,
    keywords: 'api keys mcp tokens authentication',
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
    id: 'appearance',
    label: 'Appearance',
    icon: 'E',
    path: '/workspace/settings/appearance',
    flag: null,
    primaryModes: ['workbench'],
    secondaryModes: ['guided', 'agent'],
    keywords: 'appearance theme paper night dark light obsidian legacy',
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

/**
 * Existing developer routes that were never part of the command-palette catalog.
 * Offer them only from guided mode's explicit Advanced disclosure so the
 * established workbench and agent catalogs remain unchanged.
 */
const guidedAdvancedOnlyCatalog: NavItem[] = [
  {
    id: 'ops-endpoints',
    label: 'Endpoints',
    icon: 'E',
    path: '/workspace/ops/endpoints',
    flag: 'newOps',
    primaryModes: [],
    guidedAdvanced: true,
    keywords: 'advanced ops endpoints health connectivity diagnostics',
  },
  {
    id: 'ops-logs',
    label: 'Logs',
    icon: 'L',
    path: '/workspace/ops/logs',
    flag: 'newOps',
    primaryModes: [],
    guidedAdvanced: true,
    keywords: 'advanced ops logs diagnostics correlation',
  },
  {
    id: 'cohorts',
    label: 'Cohorts',
    icon: 'C',
    path: '/workspace/metrics/cohorts',
    flag: 'newAutomation',
    primaryModes: [],
    guidedAdvanced: true,
    keywords: 'advanced metrics cohorts automation outcomes',
  },
  {
    id: 'dev-tools',
    label: 'Dev Tools',
    icon: 'D',
    path: '/workspace/dev-tools',
    flag: 'devTools',
    primaryModes: [],
    guidedAdvanced: true,
    keywords: 'advanced developer scenarios traces diagnostics',
  },
]

const activeWorkspaceMode = computed<WorkspaceMode>(() =>
  isWorkspaceMode(workspace.mode)
    ? workspace.mode
    : 'guided')

function isFeatureAvailable(item: NavItem): boolean {
  if (!item.flag) return true
  if (activeWorkspaceMode.value === 'workbench' && item.workbenchBypassesFlag) return true
  return featureFlags.isEnabled(item.flag)
}

// The command palette remains a complete escape hatch in guided mode. The
// Advanced disclosure changes visible navigation only, never route or command
// reachability.
const availableNavItems = computed(() => navCatalog.filter(isFeatureAvailable))

/**
 * Reduced IA: only sidebarPrimary items appear in the sidebar nav.
 * All other surfaces remain accessible via the command palette (Ctrl+K).
 */
const sidebarNavItems = computed(() =>
  availableNavItems.value.filter((item) => item.sidebarPrimary === true))

const guidedAdvancedNavItems = computed(() => orderGuidedAdvancedDestinations([
  ...availableNavItems.value.filter(item => item.guidedAdvanced === true),
  ...guidedAdvancedOnlyCatalog.filter(isFeatureAvailable),
]))

watch(activeWorkspaceMode, mode => {
  if (mode !== 'guided') guidedAdvancedRevealed.value = false
})

function toggleGuidedAdvanced() {
  guidedAdvancedRevealed.value = !guidedAdvancedRevealed.value
}

function switchToWorkbench() {
  guidedAdvancedRevealed.value = false
  closeMobileMenu()
  void workspace.updateMode('workbench')
}

// Note: primaryNavItems and secondaryNavItems were removed as part of sidebar IA
// reduction. All surfaces remain accessible via the command palette (Ctrl+K) through
// availableNavItems which is exposed to the parent.

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

  if (path === '/workspace/metrics' && guidedAdvancedShows('cohorts')) {
    return route.path === path
  }

  if (path === '/workspace/ops/cli') {
    return guidedAdvancedShows('ops-endpoints') || guidedAdvancedShows('ops-logs')
      ? route.path === path
      : route.path.startsWith('/workspace/ops')
  }

  return route.path.startsWith(path)
}

function guidedAdvancedShows(itemId: string): boolean {
  return activeWorkspaceMode.value === 'guided'
    && guidedAdvancedRevealed.value
    && guidedAdvancedNavItems.value.some(item => item.id === itemId)
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
          title="Taskdeck suggests changes; you decide what reaches your boards. Change the workspace mode in Preferences."
        >Review before changes</span>
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
        v-for="item in sidebarNavItems"
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

      <!-- Search opens the command palette -->
      <button
        class="td-nav-item"
        :aria-label="`Search (${commandPaletteKeys})`"
        @click="emit('open-search'); closeMobileMenu()"
      >
        <span class="td-nav-item__icon">
          <span class="material-symbols-outlined text-base">search</span>
        </span>
        <span v-if="!sidebarCollapsed" class="td-nav-item__label">Search</span>
        <span v-if="!sidebarCollapsed" class="td-nav-item__kbd">{{ commandPaletteKeys }}</span>
      </button>

      <div
        v-if="activeWorkspaceMode === 'guided'"
        class="td-sidebar__advanced"
        data-testid="guided-advanced-navigation"
      >
        <button
          type="button"
          class="td-nav-item td-nav-item--secondary"
          data-testid="guided-advanced-toggle"
          :aria-expanded="guidedAdvancedRevealed"
          :aria-label="guidedAdvancedRevealed ? 'Hide advanced navigation' : 'Show advanced navigation'"
          aria-controls="guided-advanced-destinations"
          @click="toggleGuidedAdvanced"
        >
          <span class="td-nav-item__icon">A</span>
          <span v-if="!sidebarCollapsed" class="td-nav-item__label">Advanced</span>
          <span v-if="!sidebarCollapsed" class="td-nav-item__disclosure">{{ guidedAdvancedRevealed ? 'Hide' : 'Show' }}</span>
        </button>

        <div
          v-if="guidedAdvancedRevealed"
          id="guided-advanced-destinations"
          class="td-sidebar__advanced-list"
        >
          <router-link
            v-for="item in guidedAdvancedNavItems"
            :key="item.id"
            :to="item.path"
            class="td-nav-item td-nav-item--secondary"
            :class="{ 'td-nav-item--active': isActiveRoute(item.path) }"
            :aria-current="isActiveRoute(item.path) ? 'page' : undefined"
            :aria-label="item.label"
            @click="closeMobileMenu"
          >
            <span class="td-nav-item__icon">{{ item.icon }}</span>
            <span v-if="!sidebarCollapsed" class="td-nav-item__label">{{ item.label }}</span>
          </router-link>

          <button
            type="button"
            class="td-nav-item td-nav-item--secondary"
            data-testid="switch-to-workbench"
            aria-label="Use advanced workspace"
            @click="switchToWorkbench"
          >
            <span class="td-nav-item__icon">→</span>
            <span v-if="!sidebarCollapsed" class="td-nav-item__label">Use advanced workspace</span>
          </button>
        </div>
      </div>

      <!-- Settings link at bottom of nav (above footer) -->
      <div class="td-sidebar__spacer" />
      <router-link
        v-if="featureFlags.isEnabled('newAuth')"
        to="/workspace/settings/profile"
        class="td-nav-item td-nav-item--secondary"
        :class="{ 'td-nav-item--active': isActiveRoute('/workspace/settings/profile') }"
        :aria-current="isActiveRoute('/workspace/settings/profile') ? 'page' : undefined"
        @click="closeMobileMenu"
      >
        <span class="td-nav-item__icon">S</span>
        <span v-if="!sidebarCollapsed" class="td-nav-item__label">Settings</span>
      </router-link>
      <!-- Appearance/theme is the one settings page worth a visible link: it is
           the only way for a default-'off' user to discover Paper from the
           Legacy shell (the rest of the settings cluster stays Ctrl+K-only). -->
      <router-link
        to="/workspace/settings/appearance"
        class="td-nav-item td-nav-item--secondary"
        :class="{ 'td-nav-item--active': isActiveRoute('/workspace/settings/appearance') }"
        :aria-current="isActiveRoute('/workspace/settings/appearance') ? 'page' : undefined"
        @click="closeMobileMenu"
      >
        <span class="td-nav-item__icon">E</span>
        <span v-if="!sidebarCollapsed" class="td-nav-item__label">Appearance</span>
      </router-link>
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

.td-sidebar__advanced,
.td-sidebar__advanced-list {
  display: flex;
  flex-direction: column;
  gap: 1px;
}

.td-sidebar__advanced-list {
  border-left: 1px solid var(--td-border-ghost);
  margin-left: var(--td-space-5);
}

.td-sidebar__advanced-list .td-nav-item {
  padding-left: var(--td-space-4);
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

.td-nav-item__kbd {
  margin-left: auto;
  font-family: 'Space Grotesk', monospace;
  font-size: 0.6rem;
  color: var(--td-text-tertiary);
  background: var(--td-surface-container-highest);
  border: 1px solid var(--td-border-default);
  border-radius: var(--td-radius-sm);
  padding: 1px 4px;
  letter-spacing: 0.05em;
}

.td-nav-item__disclosure {
  margin-left: auto;
  color: var(--td-text-tertiary);
  font-size: 0.6rem;
  letter-spacing: 0.05em;
}

.td-sidebar__spacer {
  flex: 1;
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
