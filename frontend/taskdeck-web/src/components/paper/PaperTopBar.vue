<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref } from 'vue'
import { useRoute, useRouter, type RouteLocationMatched } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useEscapeToClose } from '../../composables/useEscapeToClose'
import { useFeatureFlagStore } from '../../store/featureFlagStore'
import { useSessionStore } from '../../store/sessionStore'
import { useWorkspaceStore } from '../../store/workspaceStore'
import type { WorkspaceMode } from '../../types/workspace'
import { isWorkspaceMode } from '../../types/workspace'
import { formatShortcut } from '../../utils/keyboardShortcuts'
import PaperIcon from './PaperIcon.vue'
import PaperKbd from './PaperKbd.vue'
import PaperStatusPill from './PaperStatusPill.vue'

/**
 * PaperTopBar — Paper & Graphite top bar.
 *
 * Mirrors the JSX in `design_handoff_taskdeck_paper/paper/components.jsx`
 * (`TopBar`).  Builds breadcrumb segments from `route.matched`, looking for
 * `route.meta.breadcrumb` first and otherwise humanizing the route name.  The
 * The platform-aware command-palette trigger emits `palette:open` so the parent
 * shell can wire it into the existing command-palette composable.
 *
 * Right-hand controls (issue 1932 — all three used to render enabled and do
 * nothing).  NB: write issue numbers WITHOUT the leading hash in this
 * directory — the Paper Color Audit CI gate greps for hex literals and reads a
 * hash followed by four hex digits as a colour, so a hash-prefixed four-digit
 * issue number fails the build.
 *   - bell   → routes to `/workspace/notifications`
 *   - gear   → routes to `/workspace/settings/appearance`.  The glyph is a ring
 *              with radiating spokes and reads as a SUN, so the affordance a
 *              user expects from it is theme control; Appearance is the page
 *              that owns theme *and* language, and unlike the profile route it
 *              is not behind a feature flag.
 *   - avatar → a real `<button>` opening an account menu.  It used to be a
 *              `<div aria-label="Profile: D">`: unfocusable, not keyboard
 *              operable, and announced as a bare label with no action.
 *
 * Menu items are rendered only when they lead somewhere: `Profile` sits behind
 * the `newAuth` flag, and with that flag off the router guard silently bounces
 * the route to Home — an enabled-and-silent control is exactly the defect this
 * component is being fixed for, so the item is omitted instead.
 */

const emit = defineEmits<{
  'palette:open': []
  logout: []
}>()

const route = useRoute()
const router = useRouter()
const session = useSessionStore()
const workspace = useWorkspaceStore()
const featureFlags = useFeatureFlagStore()
const { t } = useI18n()

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

const commandPaletteShortcut = computed(() => formatShortcut('mod+k'))

const accountDisplayName = computed(() => {
  const name = session.username?.trim()
  return name && name !== '' ? name : avatarLetter.value
})

/**
 * The trigger's accessible name has to carry the IDENTITY, not just the verb.
 * The control it replaced announced `Profile: D`; a bare "Open account menu"
 * drops the only cue a non-sighted user had for *whose* account this is, since
 * the avatar letter is the visual-only carrier of that fact.  Composed from the
 * two catalog entries that already exist rather than a new key, so every locale
 * gets it for free and no English is hardcoded here.
 */
const accountTriggerLabel = computed(
  () =>
    `${t('shell.topbar.account.trigger')} (${t('shell.topbar.account.signedInAs', {
      name: accountDisplayName.value,
    })})`,
)

const canOpenProfile = computed(() => featureFlags.isEnabled('newAuth'))

const accountMenuOpen = ref(false)
const accountRootEl = ref<HTMLElement | null>(null)
const accountTriggerEl = ref<HTMLButtonElement | null>(null)
const accountMenuEl = ref<HTMLElement | null>(null)

function handlePaletteClick() {
  emit('palette:open')
}

function goToNotifications() {
  void router.push({ name: 'workspace-notifications' })
}

function goToAppearance() {
  void router.push({ name: 'workspace-settings-appearance' })
}

/**
 * Close on any pointer press outside the account cluster. `pointerdown` (not
 * `click`) so the menu is already gone by the time the press lands on whatever
 * is underneath, and capture phase so a stopPropagation somewhere downstream
 * cannot strand the menu open.
 */
function handleDocumentPointerDown(event: Event) {
  const target = event.target
  if (target instanceof Node && accountRootEl.value?.contains(target)) {
    return
  }
  closeAccountMenu({ restoreFocus: false })
}

function openAccountMenu() {
  if (accountMenuOpen.value) return
  accountMenuOpen.value = true
  document.addEventListener('pointerdown', handleDocumentPointerDown, true)
  void nextTick(() => {
    accountMenuEl.value?.querySelector<HTMLElement>('[role="menuitem"]')?.focus()
  })
}

function closeAccountMenu({ restoreFocus = true }: { restoreFocus?: boolean } = {}) {
  if (!accountMenuOpen.value) return
  accountMenuOpen.value = false
  document.removeEventListener('pointerdown', handleDocumentPointerDown, true)
  if (restoreFocus) {
    accountTriggerEl.value?.focus()
  }
}

function toggleAccountMenu() {
  if (accountMenuOpen.value) {
    closeAccountMenu()
    return
  }
  openAccountMenu()
}

/**
 * Close when focus leaves the account cluster entirely.  Without this, Tab (or
 * Shift+Tab) walks straight out of the menu and leaves it hanging open over the
 * page while focus is somewhere else — the pointer-outside handler never fires
 * because no pointer was ever used.  `restoreFocus: false`: the user just chose
 * where focus goes, and yanking it back to the avatar would cancel that Tab and
 * make the menu impossible to leave by keyboard.
 *
 * A null `relatedTarget` (focus left the document, e.g. the window lost focus)
 * also closes — nothing inside the menu holds focus any more either way.
 */
function handleAccountFocusOut(event: FocusEvent) {
  if (!accountMenuOpen.value) return
  const next = event.relatedTarget
  if (next instanceof Node && accountRootEl.value?.contains(next)) return
  closeAccountMenu({ restoreFocus: false })
}

useEscapeToClose(
  () => accountMenuOpen.value,
  () => closeAccountMenu(),
)

onBeforeUnmount(() => {
  document.removeEventListener('pointerdown', handleDocumentPointerDown, true)
})

function handleAccountProfile() {
  closeAccountMenu()
  void router.push({ name: 'workspace-settings-profile' })
}

function handleAccountAppearance() {
  closeAccountMenu()
  void router.push({ name: 'workspace-settings-appearance' })
}

function handleAccountSignOut() {
  closeAccountMenu()
  emit('logout')
}

/** Roving arrow-key focus — `role="menu"` promises it, so it has to be real. */
function handleAccountMenuKeydown(event: KeyboardEvent) {
  const keys = ['ArrowDown', 'ArrowUp', 'Home', 'End']
  if (!keys.includes(event.key)) return

  const items = Array.from(
    accountMenuEl.value?.querySelectorAll<HTMLElement>('[role="menuitem"]') ?? [],
  )
  if (items.length === 0) return

  event.preventDefault()
  const current = items.findIndex((item) => item === document.activeElement)
  let next = 0
  if (event.key === 'End') {
    next = items.length - 1
  } else if (event.key === 'ArrowUp') {
    next = current <= 0 ? items.length - 1 : current - 1
  } else if (event.key === 'ArrowDown') {
    next = current === -1 || current === items.length - 1 ? 0 : current + 1
  }
  items[next]?.focus()
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
      <PaperKbd>{{ commandPaletteShortcut }}</PaperKbd>
    </button>

    <PaperStatusPill kind="live" class="paper-topbar__status">SYNCED &middot; LOCAL-FIRST</PaperStatusPill>

    <div class="paper-topbar__hairline" aria-hidden="true" />

    <button
      type="button"
      class="paper-topbar__icon-btn"
      :aria-label="t('shell.topbar.notifications')"
      :title="t('shell.topbar.notifications')"
      data-topbar-action="notifications"
      @click="goToNotifications"
    >
      <PaperIcon name="bell" />
    </button>
    <button
      type="button"
      class="paper-topbar__icon-btn"
      :aria-label="t('shell.topbar.appearance')"
      :title="t('shell.topbar.appearance')"
      data-topbar-action="appearance"
      @click="goToAppearance"
    >
      <PaperIcon name="settings" />
    </button>

    <div ref="accountRootEl" class="paper-topbar__account" @focusout="handleAccountFocusOut">
      <button
        ref="accountTriggerEl"
        type="button"
        class="paper-topbar__avatar"
        :aria-label="accountTriggerLabel"
        :title="accountDisplayName"
        aria-haspopup="menu"
        :aria-expanded="accountMenuOpen"
        data-topbar-action="account"
        @click="toggleAccountMenu"
      >{{ avatarLetter }}</button>

      <!--
        The "Signed in as …" line sits OUTSIDE `role="menu"`: a menu may only
        own menuitem/menuitemradio/menuitemcheckbox/group/separator children, and
        a stray <p> inside it is an invalid owned child that assistive tech may
        skip or announce out of the menu's item count.  The identity it carries
        is not lost — it is also folded into the trigger's accessible name.
      -->
      <div v-if="accountMenuOpen" class="paper-topbar__menu">
        <p class="paper-topbar__menu-head">
          {{ t('shell.topbar.account.signedInAs', { name: accountDisplayName }) }}
        </p>
        <div
          ref="accountMenuEl"
          class="paper-topbar__menu-list"
          role="menu"
          tabindex="-1"
          :aria-label="t('shell.topbar.account.label')"
          @keydown="handleAccountMenuKeydown"
        >
          <!--
            `tabindex="-1"` on every item: `role="menu"` promises a single tab
            stop with arrow-key roving inside it, so the items must not each be
            their own tab stop.  Tab therefore leaves the cluster, which the
            focusout handler above turns into a close.
          -->
          <button
            v-if="canOpenProfile"
            type="button"
            role="menuitem"
            tabindex="-1"
            class="paper-topbar__menu-item"
            @click="handleAccountProfile"
          >{{ t('shell.topbar.account.profile') }}</button>
          <button
            type="button"
            role="menuitem"
            tabindex="-1"
            class="paper-topbar__menu-item"
            @click="handleAccountAppearance"
          >{{ t('shell.topbar.account.appearance') }}</button>
          <button
            type="button"
            role="menuitem"
            tabindex="-1"
            class="paper-topbar__menu-item paper-topbar__menu-item--signout"
            @click="handleAccountSignOut"
          >{{ t('shell.topbar.account.signOut') }}</button>
        </div>
      </div>
    </div>
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

.paper-topbar__account {
  position: relative;
  display: flex;
  align-items: center;
}

.paper-topbar__avatar {
  width: 26px;
  height: 26px;
  padding: 0;
  border-radius: 50%;
  border: 1px solid var(--line);
  background: var(--paper-card);
  display: grid;
  place-items: center;
  font-family: var(--serif);
  font-style: italic;
  font-size: 13px;
  color: var(--ink-deep);
  cursor: pointer;
}

.paper-topbar__avatar:hover,
.paper-topbar__avatar[aria-expanded='true'] {
  border-color: var(--ember);
  color: var(--ember);
}

.paper-topbar__menu {
  position: absolute;
  top: calc(100% + 8px);
  right: 0;
  z-index: 50;
  min-width: 176px;
  padding: 4px;
  border: 1px solid var(--line);
  border-radius: 4px;
  background: var(--paper-card);
  /* Theme-aware lift token — Paper Night redefines it, a literal shadow would
     stay light-theme black on the dark shell (PAPER_NIGHT_AUDIT). */
  box-shadow: var(--shadow-lift);
  font-family: var(--sans);
}

.paper-topbar__menu-head {
  padding: 6px 10px 8px;
  margin: 0;
  border-bottom: 1px solid var(--line);
  font-size: 11px;
  color: var(--faint);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 22ch;
}

.paper-topbar__menu-item {
  display: block;
  width: 100%;
  padding: 7px 10px;
  border: 0;
  border-radius: 3px;
  background: transparent;
  text-align: left;
  font-family: var(--sans);
  font-size: 12.5px;
  color: var(--ink);
  cursor: pointer;
}

.paper-topbar__menu-item:hover,
.paper-topbar__menu-item:focus-visible {
  background: var(--paper-2);
  color: var(--ember);
  outline: none;
}

.paper-topbar__menu-item--signout {
  color: var(--ink-2);
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

  .paper-topbar__account {
    flex: 0 0 26px;
  }
}
</style>
