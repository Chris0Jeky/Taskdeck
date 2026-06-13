<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { registerEscapeHandler } from '../../composables/useEscapeStack'
import { useViewportMode } from '../../composables/useViewportMode'
import { useFeatureFlagStore } from '../../store/featureFlagStore'
import { usePaperThemeStore } from '../../store/paperThemeStore'
import { useWorkspaceStore } from '../../store/workspaceStore'
import type { FeatureFlags } from '../../types/feature-flags'
import PaperIcon from './PaperIcon.vue'
import PaperStatusPill from './PaperStatusPill.vue'

/**
 * PaperSidebar — Paper & Graphite shell sidebar.
 *
 * Mirrors the JSX in `design_handoff_taskdeck_paper/paper/components.jsx`
 * (`Sidebar` + `SidebarGroup`).  Renders the three IA groups (Primary loop,
 * Workbench tools, Meta) using router-aware active state and badge counts
 * sourced from `useWorkspaceStore`.  Items whose `flag` is gated off are
 * filtered out.  Theme toggle (sun/moon) flips between paper and paper-night
 * via `paperThemeStore.toggleNight()`.
 */

type PaperNavItemBase = {
  id: string
  label: string
  glyph: string
  path: string
}

type PaperNavItem = PaperNavItemBase & {
  badgeKey?: 'inbox' | 'review'
  flag?: keyof FeatureFlags
  workbenchBypassesFlag?: boolean
  keywords?: string
}

const props = withDefaults(
  defineProps<{
    workspaceName?: string
    /** Version label shown in the footer (mono). */
    version?: string
  }>(),
  {
    workspaceName: 'Solo Workspace',
    version: 'v0.7.2',
  },
)

const emit = defineEmits<{
  logout: []
  'open-shortcuts': []
}>()

const route = useRoute()
const featureFlags = useFeatureFlagStore()
const workspace = useWorkspaceStore()
const paperTheme = usePaperThemeStore()
const { mode: viewportMode } = useViewportMode()
const mobileOpen = ref(false)
const phoneMoreOpen = ref(false)
const phoneMoreDrawerId = 'paper-phone-more-drawer'

const phoneNavItemCandidates: PaperNavItem[] = [
  { id: 'home', glyph: 'H', label: 'Home', path: '/workspace/home', keywords: '' },
  { id: 'today', glyph: 'T', label: 'Today', path: '/workspace/today', keywords: '' },
  { id: 'review', glyph: 'R', label: 'Review', path: '/workspace/review', flag: 'newAutomation', workbenchBypassesFlag: true, keywords: '' },
  { id: 'inbox', glyph: 'I', label: 'Inbox', path: '/workspace/inbox', keywords: '' },
]
const phoneNavItems = computed<PaperNavItemBase[]>(() => phoneNavItemCandidates.filter(isAvailable))

const primaryItems: PaperNavItem[] = [
  { id: 'home', label: 'Home', glyph: 'H', path: '/workspace/home', keywords: 'home start summary workspace' },
  { id: 'today', label: 'Today', glyph: 'T', path: '/workspace/today', keywords: 'today agenda daily focus overdue blocked' },
  { id: 'review', label: 'Review', glyph: 'R', path: '/workspace/review', badgeKey: 'review', flag: 'newAutomation', workbenchBypassesFlag: true, keywords: 'review proposals automations approve reject execute' },
  { id: 'boards', label: 'Boards', glyph: 'B', path: '/workspace/boards', keywords: 'boards projects workspace' },
  { id: 'inbox', label: 'Inbox', glyph: 'I', path: '/workspace/inbox', badgeKey: 'inbox', keywords: 'inbox captures triage' },
]

const workbenchItems: PaperNavItem[] = [
  { id: 'views', label: 'Views', glyph: 'V', path: '/workspace/views', keywords: 'views saved filters shortcuts blocked due week review' },
  { id: 'notifications', label: 'Notifications', glyph: 'N', path: '/workspace/notifications', keywords: 'notifications updates mention assignment' },
  { id: 'chat', label: 'Chat', glyph: 'C', path: '/workspace/automations/chat', flag: 'newAutomation', workbenchBypassesFlag: true, keywords: 'chat automation assistant board context' },
  { id: 'calendar', label: 'Calendar', glyph: 'D', path: '/workspace/calendar', keywords: 'calendar timeline planning due dates schedule deadlines' },
  { id: 'metrics', label: 'Metrics', glyph: 'M', path: '/workspace/metrics', keywords: 'metrics analytics throughput cycle time wip blocked dashboard' },
  { id: 'integrations', label: 'Integrations', glyph: 'X', path: '/workspace/integrations', keywords: 'integrations connectors inbound outbound webhook import' },
  { id: 'activity', label: 'Activity', glyph: 'Y', path: '/workspace/activity', flag: 'newActivity', workbenchBypassesFlag: true, keywords: 'activity audit history events' },
  { id: 'ops', label: 'Ops', glyph: 'O', path: '/workspace/ops/cli', flag: 'newOps', workbenchBypassesFlag: true, keywords: 'ops logs cli endpoints' },
]

const metaItems: PaperNavItem[] = [
  { id: 'settings', label: 'Settings', glyph: 'S', path: '/workspace/settings/profile', flag: 'newAuth', workbenchBypassesFlag: true, keywords: 'settings profile password account' },
  { id: 'api-keys', label: 'API Keys', glyph: 'K', path: '/workspace/settings/api-keys', keywords: 'api keys mcp tokens authentication' },
  { id: 'preferences', label: 'Preferences', glyph: 'P', path: '/workspace/settings/preferences', keywords: 'preferences notifications' },
  { id: 'appearance', label: 'Appearance', glyph: 'E', path: '/workspace/settings/appearance', keywords: 'appearance theme paper night dark light obsidian legacy' },
  { id: 'shortcuts', label: 'Shortcuts', glyph: '?', path: '#shortcuts' },
  { id: 'logout', label: 'Logout', glyph: '→', path: '#logout' },
]

const commandOnlyItems: PaperNavItem[] = [
  { id: 'agents', label: 'Agents', glyph: 'G', path: '/workspace/agents', keywords: 'agents profiles runs automation agent mode' },
  { id: 'access', label: 'Access', glyph: 'A', path: '/workspace/settings/access', flag: 'newAccess', workbenchBypassesFlag: true, keywords: 'access board sharing permissions' },
  { id: 'archive', label: 'Archive', glyph: 'Z', path: '/workspace/archive', flag: 'newArchive', workbenchBypassesFlag: true, keywords: 'archive restore hidden boards' },
]

function isAvailable(item: PaperNavItem): boolean {
  if (!item.flag) return true
  if (workspace.mode === 'workbench' && item.workbenchBypassesFlag) return true
  return featureFlags.isEnabled(item.flag)
}

const visiblePrimary = computed(() => primaryItems.filter(isAvailable))
const visibleWorkbench = computed(() => workbenchItems.filter(isAvailable))
const visibleMeta = computed(() => metaItems.filter(isAvailable))
const availableNavItems = computed(() =>
  [...visiblePrimary.value, ...visibleWorkbench.value, ...visibleMeta.value]
    .concat(commandOnlyItems.filter(isAvailable))
    .filter((item) => !item.path.startsWith('#'))
    .map((item) => ({
      id: item.id,
      label: item.label,
      icon: item.glyph,
      path: item.path,
      keywords: item.keywords,
    })),
)

function badgeFor(item: PaperNavItem): number {
  if (item.badgeKey === 'inbox') return workspace.inboxBadgeCount
  if (item.badgeKey === 'review') return workspace.reviewBadgeCount
  return 0
}

function isActive(item: PaperNavItemBase): boolean {
  if (item.path.startsWith('#')) return false
  if (item.path === '/workspace/home') return route.path === item.path
  if (item.path === '/workspace/review') {
    return isCurrentOrChild('/workspace/review')
      || isCurrentOrChild('/workspace/automations/proposals')
      || isCurrentOrChild('/workspace/automations/queue')
  }
  if (item.path === '/workspace/ops/cli') return isCurrentOrChild('/workspace/ops')
  return isCurrentOrChild(item.path)
}

function isCurrentOrChild(path: string): boolean {
  return route.path === path || route.path.startsWith(`${path}/`)
}

const workspaceInitial = computed(() =>
  (props.workspaceName?.trim().charAt(0) || 'S').toUpperCase(),
)

const themeToggleLabel = computed(() =>
  paperTheme.activeClass === 'paper-night' ? 'Switch to light Paper theme' : 'Switch to dark Paper theme',
)

const themeIcon = computed<'sun' | 'moon'>(() =>
  paperTheme.activeClass === 'paper-night' ? 'sun' : 'moon',
)

function handleMetaClick(item: PaperNavItem, event: MouseEvent) {
  if (!item.path.startsWith('#')) return
  event.preventDefault()
  closeMobileMenu()
  if (item.id === 'logout') emit('logout')
  else if (item.id === 'shortcuts') emit('open-shortcuts')
}

function handleThemeToggle() {
  paperTheme.toggleNight()
}

function closeMobileMenu() {
  mobileOpen.value = false
}

function toggleMobileMenu() {
  if (viewportMode.value === 'phone') {
    togglePhoneMore()
    return
  }

  mobileOpen.value = !mobileOpen.value
}

function closePhoneMore() {
  phoneMoreOpen.value = false
}

function togglePhoneMore() {
  phoneMoreOpen.value = !phoneMoreOpen.value
}

function handlePhoneMetaClick(item: PaperNavItem, event: MouseEvent) {
  if (!item.path.startsWith('#')) {
    closePhoneMore()
    return
  }

  event.preventDefault()
  closePhoneMore()
  if (item.id === 'logout') emit('logout')
  else if (item.id === 'shortcuts') emit('open-shortcuts')
}

watch(mobileOpen, (isOpen, _, onCleanup) => {
  if (!isOpen) return

  document.body.style.overflow = 'hidden'
  const unregisterEscape = registerEscapeHandler(closeMobileMenu)

  onCleanup(() => {
    document.body.style.overflow = ''
    unregisterEscape()
  })
})

watch(phoneMoreOpen, (isOpen, _, onCleanup) => {
  if (!isOpen) return

  document.body.style.overflow = 'hidden'
  const unregisterEscape = registerEscapeHandler(closePhoneMore)

  // Trap focus: mark background content inert so keyboard cannot escape drawer
  const mainContent = document.getElementById('td-main-content')
  if (mainContent) mainContent.setAttribute('inert', '')

  onCleanup(() => {
    document.body.style.overflow = ''
    unregisterEscape()
    if (mainContent) mainContent.removeAttribute('inert')
  })
})

watch(() => route.path, () => {
  closePhoneMore()
})

onUnmounted(() => {
  if (mobileOpen.value || phoneMoreOpen.value) {
    document.body.style.overflow = ''
  }
})

defineExpose({
  availableNavItems,
  visiblePrimary,
  visibleWorkbench,
  visibleMeta,
  mobileOpen,
  phoneMoreOpen,
  toggleMobileMenu,
  closeMobileMenu,
  togglePhoneMore,
  closePhoneMore,
  viewportMode,
})
</script>

<template>
  <!-- Phone: bottom tab bar + More drawer for meta navigation -->
  <template v-if="viewportMode === 'phone'">
    <nav
      class="paper-bottombar"
      role="navigation"
      aria-label="Workspace navigation"
      data-paper-sidebar
      data-paper-bottombar
    >
      <router-link
        v-for="item in phoneNavItems"
        :key="item.id"
        :to="item.path"
        class="paper-bottombar__tab"
        :class="{ 'paper-bottombar__tab--active': isActive(item) }"
        :aria-current="isActive(item) ? 'page' : undefined"
        :aria-label="item.label"
      >
        <span class="paper-bottombar__glyph">{{ item.glyph }}</span>
      </router-link>
      <button
        type="button"
        class="paper-bottombar__tab"
        :class="{ 'paper-bottombar__tab--active': phoneMoreOpen }"
        aria-label="More"
        :aria-expanded="phoneMoreOpen ? 'true' : 'false'"
        :aria-controls="phoneMoreDrawerId"
        @click="togglePhoneMore"
      >
        <span class="paper-bottombar__glyph paper-bottombar__glyph--more">…</span>
      </button>
    </nav>

    <div
      v-if="phoneMoreOpen"
      class="paper-sidebar-overlay"
      aria-hidden="true"
      @click="closePhoneMore"
    />
    <nav
      v-if="phoneMoreOpen"
      :id="phoneMoreDrawerId"
      class="paper-sidebar paper-sidebar--phone-drawer"
      role="navigation"
      aria-label="More navigation"
      data-paper-phone-drawer
    >
      <div class="paper-sidebar__group" data-group="workbench">
        <div class="paper-sidebar__group-label">Workbench</div>
        <ul class="paper-sidebar__list">
          <li v-for="item in visibleWorkbench" :key="item.id">
            <router-link
              :to="item.path"
              class="paper-sidebar__item"
              :class="{ 'paper-sidebar__item--active': isActive(item) }"
              @click="closePhoneMore"
            >
              <span class="paper-sidebar__glyph">{{ item.glyph }}</span>
              <span class="paper-sidebar__label">{{ item.label }}</span>
            </router-link>
          </li>
        </ul>
      </div>
      <div class="paper-sidebar__group paper-sidebar__group--muted" data-group="meta">
        <ul class="paper-sidebar__list">
          <li v-for="item in visibleMeta" :key="item.id">
            <router-link
              v-if="!item.path.startsWith('#')"
              :to="item.path"
              class="paper-sidebar__item"
              :class="{ 'paper-sidebar__item--active': isActive(item) }"
              :aria-current="isActive(item) ? 'page' : undefined"
              @click="closePhoneMore"
            >
              <span class="paper-sidebar__glyph">{{ item.glyph }}</span>
              <span class="paper-sidebar__label">{{ item.label }}</span>
            </router-link>
            <button
              v-else
              type="button"
              class="paper-sidebar__item"
              @click="handlePhoneMetaClick(item, $event)"
            >
              <span class="paper-sidebar__glyph">{{ item.glyph }}</span>
              <span class="paper-sidebar__label">{{ item.label }}</span>
            </button>
          </li>
        </ul>
      </div>
    </nav>
  </template>

  <!-- Tablet: 60px icon-only rail -->
  <template v-else-if="viewportMode === 'tablet'">
    <nav
      class="paper-sidebar paper-sidebar--rail"
      role="navigation"
      aria-label="Workspace navigation"
      data-paper-sidebar
      data-paper-rail
    >
      <div class="paper-sidebar__header">
        <div class="paper-sidebar__brand">Td</div>
      </div>

      <div class="paper-sidebar__group" data-group="primary">
        <ul class="paper-sidebar__list">
          <li v-for="item in visiblePrimary" :key="item.id">
            <router-link
              :to="item.path"
              class="paper-sidebar__item"
              :class="{ 'paper-sidebar__item--active': isActive(item) }"
              :aria-current="isActive(item) ? 'page' : undefined"
              :aria-label="item.label"
            >
              <span class="paper-sidebar__glyph">{{ item.glyph }}</span>
            </router-link>
          </li>
        </ul>
      </div>

      <div class="paper-sidebar__group" data-group="workbench">
        <ul class="paper-sidebar__list">
          <li v-for="item in visibleWorkbench" :key="item.id">
            <router-link
              :to="item.path"
              class="paper-sidebar__item"
              :class="{ 'paper-sidebar__item--active': isActive(item) }"
              :aria-current="isActive(item) ? 'page' : undefined"
              :aria-label="item.label"
            >
              <span class="paper-sidebar__glyph">{{ item.glyph }}</span>
            </router-link>
          </li>
        </ul>
      </div>

      <div class="paper-sidebar__spacer" />

      <div class="paper-sidebar__group paper-sidebar__group--muted" data-group="meta">
        <ul class="paper-sidebar__list">
          <li v-for="item in visibleMeta" :key="item.id">
            <router-link
              v-if="!item.path.startsWith('#')"
              :to="item.path"
              class="paper-sidebar__item paper-sidebar__item--muted"
              :class="{ 'paper-sidebar__item--active': isActive(item) }"
              :aria-current="isActive(item) ? 'page' : undefined"
              :aria-label="item.label"
            >
              <span class="paper-sidebar__glyph">{{ item.glyph }}</span>
            </router-link>
            <button
              v-else
              type="button"
              class="paper-sidebar__item paper-sidebar__item--muted"
              :aria-label="item.label"
              @click="handleMetaClick(item, $event)"
            >
              <span class="paper-sidebar__glyph">{{ item.glyph }}</span>
            </button>
          </li>
        </ul>
      </div>

      <div class="paper-sidebar__footer">
        <button
          type="button"
          class="paper-sidebar__theme-toggle"
          :aria-label="themeToggleLabel"
          @click="handleThemeToggle"
        >
          <PaperIcon :name="themeIcon" :label="themeToggleLabel" />
        </button>
      </div>
    </nav>
  </template>

  <!-- Desktop: full sidebar -->
  <template v-else>
    <div
      v-if="mobileOpen"
      class="paper-sidebar-overlay"
      aria-hidden="true"
      @click="closeMobileMenu"
    />
    <nav
      class="paper-sidebar"
      :class="{ 'paper-sidebar--mobile-open': mobileOpen }"
      role="navigation"
      aria-label="Workspace navigation"
      data-paper-sidebar
    >
      <div class="paper-sidebar__header">
        <div class="paper-sidebar__brand">Taskdeck</div>
        <div class="tk-eyebrow paper-sidebar__eyebrow">
          Precision Mode <span class="paper-sidebar__eyebrow-active">&middot; active</span>
        </div>
      </div>

      <button type="button" class="paper-sidebar__workspace" aria-label="Switch workspace">
        <span class="paper-sidebar__workspace-glyph">{{ workspaceInitial }}</span>
        <span class="paper-sidebar__workspace-name">{{ workspaceName }}</span>
        <PaperIcon name="chevronDown" />
      </button>

      <div class="paper-sidebar__group" data-group="primary">
        <div class="tk-eyebrow paper-sidebar__group-label">Primary loop</div>
        <ul class="paper-sidebar__list">
          <li v-for="item in visiblePrimary" :key="item.id">
            <router-link
              :to="item.path"
              class="paper-sidebar__item"
              :class="{ 'paper-sidebar__item--active': isActive(item) }"
              :aria-current="isActive(item) ? 'page' : undefined"
              @click="closeMobileMenu"
            >
              <span class="paper-sidebar__glyph">{{ item.glyph }}</span>
              <span class="paper-sidebar__label">{{ item.label }}</span>
              <span
                v-if="badgeFor(item) > 0"
                class="paper-sidebar__badge"
                :aria-label="`${item.label}: ${badgeFor(item)} pending`"
              >&middot; {{ badgeFor(item) }}</span>
            </router-link>
          </li>
        </ul>
      </div>

      <div class="paper-sidebar__group" data-group="workbench">
        <div class="tk-eyebrow paper-sidebar__group-label">Workbench tools</div>
        <ul class="paper-sidebar__list">
          <li v-for="item in visibleWorkbench" :key="item.id">
            <router-link
              :to="item.path"
              class="paper-sidebar__item"
              :class="{ 'paper-sidebar__item--active': isActive(item) }"
              :aria-current="isActive(item) ? 'page' : undefined"
              @click="closeMobileMenu"
            >
              <span class="paper-sidebar__glyph">{{ item.glyph }}</span>
              <span class="paper-sidebar__label">{{ item.label }}</span>
            </router-link>
          </li>
        </ul>
      </div>

      <div class="paper-sidebar__spacer" />

      <div class="paper-sidebar__group paper-sidebar__group--muted" data-group="meta">
        <ul class="paper-sidebar__list">
          <li v-for="item in visibleMeta" :key="item.id">
            <router-link
              v-if="!item.path.startsWith('#')"
              :to="item.path"
              class="paper-sidebar__item paper-sidebar__item--muted"
              :class="{ 'paper-sidebar__item--active': isActive(item) }"
              :aria-current="isActive(item) ? 'page' : undefined"
              @click="closeMobileMenu"
            >
              <span class="paper-sidebar__glyph">{{ item.glyph }}</span>
              <span class="paper-sidebar__label">{{ item.label }}</span>
            </router-link>
            <a
              v-else
              href="#"
              class="paper-sidebar__item paper-sidebar__item--muted"
              @click="handleMetaClick(item, $event)"
            >
              <span class="paper-sidebar__glyph">{{ item.glyph }}</span>
              <span class="paper-sidebar__label">{{ item.label }}</span>
            </a>
          </li>
        </ul>
      </div>

      <div class="paper-sidebar__footer">
        <div class="paper-sidebar__footer-status">
          <PaperStatusPill kind="live">SYSTEM LIVE</PaperStatusPill>
          <span class="paper-sidebar__version">{{ version }}</span>
        </div>
        <button
          type="button"
          class="paper-sidebar__theme-toggle"
          :aria-label="themeToggleLabel"
          @click="handleThemeToggle"
        >
          <PaperIcon :name="themeIcon" :label="themeToggleLabel" />
        </button>
      </div>
    </nav>
  </template>
</template>

<style scoped>
/* =========================================================
   Desktop: full sidebar
   ========================================================= */
.paper-sidebar {
  width: 232px;
  flex: none;
  background: var(--paper-2);
  border-right: 1px solid var(--line);
  padding: 20px 0 16px;
  display: flex;
  flex-direction: column;
  font-family: var(--sans);
  position: relative;
  min-height: 100vh;
}

.paper-sidebar-overlay {
  display: none;
}

.paper-sidebar__header {
  padding: 0 20px 18px;
  border-bottom: 1px solid var(--line-soft);
}

.paper-sidebar__brand {
  font-family: var(--serif);
  font-weight: 500;
  font-size: 18px;
  letter-spacing: -0.01em;
  color: var(--ink-deep);
}

.paper-sidebar__eyebrow {
  margin-top: 4px;
}

.paper-sidebar__eyebrow-active {
  color: var(--ember);
}

/* Workspace switcher */
.paper-sidebar__workspace {
  margin: 12px 12px 6px;
  padding: 8px 10px;
  display: flex;
  align-items: center;
  gap: 10px;
  background: transparent;
  border: 1px solid var(--line-soft);
  border-radius: 4px;
  cursor: pointer;
  text-align: left;
  font-family: inherit;
  color: inherit;
}

.paper-sidebar__workspace-glyph {
  width: 22px;
  height: 22px;
  border-radius: 2px;
  border: 1px solid var(--line);
  display: grid;
  place-items: center;
  font-family: var(--serif);
  font-style: italic;
  font-size: 12px;
  color: var(--ink-deep);
  background: var(--paper-card);
}

.paper-sidebar__workspace-name {
  flex: 1;
  font-size: 12px;
  font-weight: 500;
  color: var(--ink);
}

/* Groups */
.paper-sidebar__group {
  padding: 10px 0 4px;
}

.paper-sidebar__group-label {
  padding: 8px 20px 6px;
  color: var(--faint);
}

.paper-sidebar__spacer {
  flex: 1;
}

.paper-sidebar__list {
  list-style: none;
  margin: 0;
  padding: 0;
}

/* Items */
.paper-sidebar__item {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 6px 20px;
  height: 36px;
  text-decoration: none;
  color: var(--ink-2);
  font-size: 12.5px;
  font-weight: 400;
  font-family: var(--sans);
  width: 100%;
  border: 0;
  border-left: 2px solid transparent;
  background: transparent;
  position: relative;
  cursor: pointer;
  text-align: left;
}

.paper-sidebar__item:hover {
  color: var(--ink-deep);
  background: linear-gradient(90deg, var(--ember-bloom) 0%, transparent 50%);
}

.paper-sidebar__item--muted {
  color: var(--mute);
}

.paper-sidebar__item--active {
  color: var(--ink-deep);
  font-weight: 600;
  border-left-color: var(--ember);
  background: linear-gradient(90deg, var(--ember-bloom) 0%, transparent 70%);
}

.paper-sidebar__item--active .paper-sidebar__glyph {
  color: var(--ember);
}

.paper-sidebar__item--active .paper-sidebar__badge {
  color: var(--ember);
}

.paper-sidebar__glyph {
  font-family: var(--mono);
  font-size: 10.5px;
  font-weight: 500;
  color: var(--faint);
  width: 14px;
  text-align: center;
  letter-spacing: 0;
}

.paper-sidebar__label {
  flex: 1;
}

.paper-sidebar__badge {
  font-family: var(--mono);
  font-size: 10px;
  color: var(--mute);
}

/* Footer */
.paper-sidebar__footer {
  margin: 8px 12px 0;
  padding: 10px 10px 0;
  border-top: 1px solid var(--line-soft);
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
}

.paper-sidebar__footer-status {
  display: flex;
  flex-direction: column;
  gap: 4px;
  font-family: var(--mono);
  font-size: 9.5px;
  letter-spacing: 0.14em;
}

.paper-sidebar__version {
  color: var(--faint);
}

.paper-sidebar__theme-toggle {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  padding: 0;
  background: transparent;
  border: 1px solid var(--line-soft);
  border-radius: 4px;
  color: var(--ink-2);
  cursor: pointer;
}

.paper-sidebar__theme-toggle:hover {
  border-color: var(--line);
  color: var(--ember);
}

@media (max-width: 640px) {
  .paper-sidebar:not(.paper-sidebar--rail):not(.paper-sidebar--phone-drawer) {
    position: fixed;
    top: 0;
    left: 0;
    bottom: 0;
    height: auto;
    transform: translateX(-100%);
    transition: transform 0.25s ease;
    z-index: 50;
  }

  .paper-sidebar--mobile-open {
    transform: translateX(0);
  }

  .paper-sidebar-overlay {
    display: block;
    position: fixed;
    inset: 0;
    background: rgba(26, 24, 20, 0.42);
    z-index: 45;
  }

  .paper-sidebar__item {
    min-height: 44px;
  }
}

@media (prefers-reduced-motion: reduce) {
  .paper-sidebar {
    transition: none;
  }
}

/* =========================================================
   Tablet: 60px icon-only rail
   ========================================================= */
.paper-sidebar--rail {
  width: 60px;
  padding: 16px 0 12px;
  align-items: center;
}

.paper-sidebar--rail .paper-sidebar__header {
  padding: 0 0 14px;
  text-align: center;
}

.paper-sidebar--rail .paper-sidebar__item {
  justify-content: center;
  padding: 6px 0;
  height: 36px;
  width: 36px;
  border-left: none;
  border-radius: var(--r-2);
}

.paper-sidebar--rail .paper-sidebar__item:hover {
  background: var(--ember-bloom);
}

.paper-sidebar--rail .paper-sidebar__item--active {
  background: var(--ember-bloom);
  border-left: none;
}

.paper-sidebar--rail .paper-sidebar__item--active .paper-sidebar__glyph {
  color: var(--ember);
}

.paper-sidebar--rail .paper-sidebar__glyph {
  width: auto;
  font-size: 12px;
}

.paper-sidebar--rail .paper-sidebar__footer {
  margin: 4px 0 0;
  padding: 8px 0 0;
  justify-content: center;
}

/* =========================================================
   Phone: bottom tab bar
   ========================================================= */
.paper-bottombar {
  position: fixed;
  bottom: 0;
  left: 0;
  right: 0;
  z-index: 40;
  display: flex;
  align-items: center;
  justify-content: space-around;
  min-height: 56px;
  padding-bottom: var(--paper-safe-bottom, env(safe-area-inset-bottom, 0px));
  background: var(--paper-card);
  border-top: 1px solid var(--line);
  font-family: var(--mono);
}

.paper-bottombar__tab {
  display: flex;
  align-items: center;
  justify-content: center;
  flex: 1;
  height: 100%;
  text-decoration: none;
  color: var(--mute);
  transition: color var(--d-quick, 140ms) var(--ease-paper, ease);
}

.paper-bottombar__tab--active {
  color: var(--ember);
}

.paper-bottombar__glyph {
  font-family: var(--mono);
  font-size: 16px;
  font-weight: 600;
  letter-spacing: 0;
}

@media (prefers-reduced-motion: reduce) {
  .paper-bottombar__tab {
    transition: none;
  }
}

.paper-bottombar__glyph--more {
  font-size: 18px;
  letter-spacing: 2px;
}

/* Phone: slide-up drawer for workbench/meta items */
.paper-sidebar--phone-drawer {
  position: fixed;
  bottom: calc(56px + var(--paper-safe-bottom, env(safe-area-inset-bottom, 0px)));
  left: 0;
  right: 0;
  width: 100%;
  min-height: 0;
  height: auto;
  max-height: 60vh;
  overflow-y: auto;
  z-index: 50;
  border-top: 1px solid var(--line);
  border-right: none;
  border-radius: 12px 12px 0 0;
  padding: 12px 0 8px;
}
</style>
