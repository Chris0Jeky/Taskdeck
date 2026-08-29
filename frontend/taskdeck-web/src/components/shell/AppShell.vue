<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useSessionStore } from '../../store/sessionStore'
import { useWorkspaceStore } from '../../store/workspaceStore'
import { usePaperThemeStore } from '../../store/paperThemeStore'
import { useCaptureQueueSync } from '../../composables/useCaptureQueueSync'
import { registerEscapeHandler } from '../../composables/useEscapeStack'
import { useViewportMode } from '../../composables/useViewportMode'
import {
  APP_SHELL_SHORTCUT_BINDINGS,
  type AppShellShortcutBinding,
  type ShortcutStroke,
} from '../../utils/keyboardShortcuts'
import CaptureModal from '../common/CaptureModal.vue'
import OfflineBanner from './OfflineBanner.vue'
import SwUpdatePrompt from './SwUpdatePrompt.vue'
import ShellSidebar from './ShellSidebar.vue'
import ShellTopbar from './ShellTopbar.vue'
import ShellCommandPalette from './ShellCommandPalette.vue'
import ShellKeyboardHelp from './ShellKeyboardHelp.vue'
import PaperSidebar from '../paper/PaperSidebar.vue'
import PaperTopBar from '../paper/PaperTopBar.vue'
import PaperCommandPalette from '../paper/PaperCommandPalette.vue'
import PaperShortcutsOverlay from '../paper/PaperShortcutsOverlay.vue'
import ErrorBoundary from '../ErrorBoundary.vue'
import type { CommandItem } from './ShellCommandPalette.vue'

type SidebarNavItem = {
  label: string
  icon: string
  path: string
  keywords?: string
}

type SidebarRef = {
  availableNavItems: SidebarNavItem[]
  toggleMobileMenu?: () => void
}

const router = useRouter()
const session = useSessionStore()
const workspace = useWorkspaceStore()
const paperTheme = usePaperThemeStore()
const { mode: viewportMode } = useViewportMode()

const { pendingCount: captureQueuePending, syncing: captureQueueSyncing } = useCaptureQueueSync()

const sidebarRef = ref<SidebarRef | null>(null)
const isPaperNarrow = computed(() => paperTheme.isOn && viewportMode.value !== 'desktop')
const isPaperPhone = computed(() => paperTheme.isOn && viewportMode.value === 'phone')

const showCommandPalette = ref(false)
const showKeyboardHelp = ref(false)
const showCaptureModal = ref(false)

// ── Capture modal ──

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

// ── Command palette ──

const commandItems = computed<CommandItem[]>(() => {
  const navItems = sidebarRef.value?.availableNavItems ?? []
  const navigationItems = navItems.map((item) => ({
    id: `nav:${item.path}`,
    label: paperTheme.isOn ? `Go to ${item.label}` : item.label,
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

function openCommandPalette() {
  showCommandPalette.value = true
}

function closeCommandPalette() {
  showCommandPalette.value = false
}

function handleCommandActivate(item: CommandItem) {
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

function handleNavigateToBoard(boardId: string) {
  void router.push(`/workspace/boards/${boardId}`)
  closeCommandPalette()
}

function handleNavigateToCard(boardId: string, _cardId: string) {
  // Navigate to the board containing the card
  void router.push(`/workspace/boards/${boardId}`)
  closeCommandPalette()
}

// ── Keyboard shortcuts ──

function isTextEntryTarget(target: EventTarget | null): boolean {
  if (!(target instanceof Element)) return false

  const selector = 'input, textarea, select, [contenteditable]:not([contenteditable="false"])'
  return target.matches(selector) || target.closest(selector) !== null
}

const KEYBOARD_OWNING_SURFACE_SELECTOR = [
  'dialog[open]',
  '[role="dialog"]',
  '[role="alertdialog"]',
  '[aria-modal="true"]',
].join(', ')

function hasActiveKeyboardOwningSurface(): boolean {
  if (typeof document === 'undefined') return false

  return Array.from(document.querySelectorAll<HTMLElement>(KEYBOARD_OWNING_SURFACE_SELECTOR))
    .some((surface) => {
      if (!surface.isConnected) return false
      if (surface.closest('[hidden], [aria-hidden="true"], [inert]')) return false

      const style = window.getComputedStyle(surface)
      return style.display !== 'none' && style.visibility !== 'hidden'
    })
}

const CHORD_TIMEOUT_MS = 1_000
let pendingChord: AppShellShortcutBinding | null = null
let chordTimer: ReturnType<typeof window.setTimeout> | null = null

function clearPendingChord() {
  pendingChord = null
  if (chordTimer !== null) {
    window.clearTimeout(chordTimer)
    chordTimer = null
  }
}

function strokeMatches(event: KeyboardEvent, stroke: ShortcutStroke): boolean {
  const modPressed = event.ctrlKey || event.metaKey
  const shiftMatches = stroke.shift === undefined || stroke.shift === event.shiftKey
  return event.key.toLowerCase() === stroke.key.toLowerCase() &&
    modPressed === Boolean(stroke.mod) &&
    event.altKey === Boolean(stroke.alt) &&
    shiftMatches
}

function consumeShortcut(event: KeyboardEvent) {
  event.preventDefault()
  // AppShell owns the workspace-level keys. Capture-phase handling keeps a
  // board-local `H` listener from also moving selection before Home navigation.
  event.stopImmediatePropagation()
}

function runAppShellShortcut(binding: AppShellShortcutBinding) {
  switch (binding.action.type) {
    case 'navigate':
      showKeyboardHelp.value = false
      closeCommandPalette()
      void router.push(binding.action.path)
      return
    case 'command-palette':
      if (showCommandPalette.value) closeCommandPalette()
      else openCommandPalette()
      return
    case 'quick-capture':
      openCaptureModal()
      return
    case 'keyboard-help':
      showKeyboardHelp.value = !showKeyboardHelp.value
      return
  }
}

function handleKeydown(event: KeyboardEvent) {
  if (event.isComposing) {
    clearPendingChord()
    return
  }

  const textEntryTarget = isTextEntryTarget(event.target)
  const keyboardOwningSurfaceActive = hasActiveKeyboardOwningSurface()

  if (keyboardOwningSurfaceActive) clearPendingChord()

  if (pendingChord) {
    const chord = pendingChord
    clearPendingChord()
    const nextStroke = chord.sequence[1]
    if (!textEntryTarget && nextStroke && strokeMatches(event, nextStroke)) {
      consumeShortcut(event)
      runAppShellShortcut(chord)
      return
    }
  }

  const direct = APP_SHELL_SHORTCUT_BINDINGS.find((binding) =>
    binding.sequence.length === 1 &&
    strokeMatches(event, binding.sequence[0]!) &&
    (binding.action.type !== 'navigate' || !keyboardOwningSurfaceActive) &&
    (!textEntryTarget || binding.allowInTextEntry === true),
  )
  if (direct) {
    consumeShortcut(event)
    runAppShellShortcut(direct)
    return
  }

  if (textEntryTarget || keyboardOwningSurfaceActive) return

  const chord = APP_SHELL_SHORTCUT_BINDINGS.find((binding) =>
    binding.sequence.length > 1 && strokeMatches(event, binding.sequence[0]!),
  )
  if (chord) {
    consumeShortcut(event)
    pendingChord = chord
    chordTimer = window.setTimeout(clearPendingChord, CHORD_TIMEOUT_MS)
  }
}

// ── Escape stack registration ──

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

// ── Lifecycle ──

function handleLogout() {
  session.logout()
  void router.push('/login')
}

onMounted(() => {
  window.addEventListener('keydown', handleKeydown, true)
})

function hydratePreferencesIfNeeded() {
  if (session.isAuthenticated && !workspace.preferencesHydrated && !workspace.preferenceLoading) {
    void workspace.hydratePreferences()
  }
}

watch(
  () => session.isAuthenticated,
  (isAuthenticated) => {
    if (!isAuthenticated) {
      workspace.resetForLogout()
      return
    }

    if (!workspace.hasHomeSummary && !workspace.homeLoading) {
      void workspace.fetchHomeSummary().catch(() => {
        hydratePreferencesIfNeeded()
      })
      return
    }

    hydratePreferencesIfNeeded()
  },
  { immediate: true },
)

onUnmounted(() => {
  clearPendingChord()
  window.removeEventListener('keydown', handleKeydown, true)
})
</script>

<template>
  <div
    class="td-shell"
    :class="{
      'td-shell--paper': paperTheme.isOn,
      'td-shell--paper-phone': isPaperPhone,
    }"
  >
    <PaperSidebar
      v-if="paperTheme.isOn"
      ref="sidebarRef"
      @logout="handleLogout"
      @open-shortcuts="showKeyboardHelp = true"
    />
    <ShellSidebar
      v-else
      ref="sidebarRef"
      :is-authenticated="session.isAuthenticated"
      @logout="handleLogout"
      @show-keyboard-help="showKeyboardHelp = true"
      @open-search="openCommandPalette"
    />

    <div class="td-main-container">
      <OfflineBanner />
      <Transition name="offline-banner">
        <div
          v-if="captureQueuePending > 0"
          class="td-capture-queue-banner"
          role="status"
          aria-live="polite"
        >
          <span class="material-symbols-outlined td-capture-queue-banner__icon" aria-hidden="true">
            {{ captureQueueSyncing ? 'sync' : 'pending' }}
          </span>
          <span class="td-capture-queue-banner__text">
            {{ captureQueueSyncing ? 'Syncing' : captureQueuePending }}
            {{ captureQueuePending === 1 ? 'capture' : 'captures' }} queued
          </span>
        </div>
      </Transition>
      <SwUpdatePrompt />
      <div v-if="!isPaperNarrow" class="td-mobile-topbar">
        <button
          class="td-mobile-topbar__hamburger"
          aria-label="Open navigation menu"
          @click="sidebarRef?.toggleMobileMenu?.()"
        >
          <span class="material-symbols-outlined">menu</span>
        </button>
        <span class="td-mobile-topbar__title">Taskdeck</span>
      </div>

      <PaperTopBar
        v-if="paperTheme.isOn"
        @palette:open="openCommandPalette"
        @logout="handleLogout"
      />
      <ShellTopbar v-else @open-command-palette="openCommandPalette" />

      <main id="td-main-content" class="td-content" tabindex="-1">
        <!--
          Per-view ErrorBoundary keeps the sidebar and topbar usable when a
          single route component crashes. The outer boundary in App.vue is
          the last-resort backstop for crashes in AppShell itself.
        -->
        <ErrorBoundary>
          <router-view />
        </ErrorBoundary>
      </main>
    </div>

    <PaperCommandPalette
      v-if="paperTheme.isOn"
      :visible="showCommandPalette"
      :items="commandItems"
      @close="closeCommandPalette"
      @activate="handleCommandActivate"
    />
    <ShellCommandPalette
      v-else
      :visible="showCommandPalette"
      :items="commandItems"
      @close="closeCommandPalette"
      @activate="handleCommandActivate"
      @navigate-to-board="handleNavigateToBoard"
      @navigate-to-card="handleNavigateToCard"
    />

    <PaperShortcutsOverlay
      v-if="paperTheme.isOn"
      :visible="showKeyboardHelp"
      @close="showKeyboardHelp = false"
    />
    <ShellKeyboardHelp
      v-else
      :visible="showKeyboardHelp"
      @close="showKeyboardHelp = false"
    />

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
  background: var(--td-surface-base);
}

.td-main-container {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.td-content {
  flex: 1;
  overflow-y: auto;
  padding: var(--td-space-8);
  background: var(--td-surface-base);
}

/* ─── Capture queue banner ─── */
.td-capture-queue-banner {
  display: flex;
  align-items: center;
  gap: var(--td-space-3);
  padding: var(--td-space-2) var(--td-space-4);
  background: var(--td-color-info-light, #e8f4fd);
  border-bottom: 1px solid var(--td-color-info, #64b5f6);
  color: var(--td-color-info, #1565c0);
  font-size: var(--td-font-sm);
  font-weight: 500;
  flex-shrink: 0;
}

.td-capture-queue-banner__icon {
  font-size: 18px;
  flex-shrink: 0;
}

.td-capture-queue-banner__text {
  line-height: 1.4;
}

/* ─── Mobile top bar (hamburger) ─── */
.td-mobile-topbar {
  display: none;
}

@media (max-width: 640px) {
  .td-mobile-topbar {
    display: flex;
    align-items: center;
    gap: var(--td-space-3);
    padding: var(--td-space-3) var(--td-space-4);
    background: var(--td-surface-container);
    border-bottom: 1px solid var(--td-border-ghost);
  }

  .td-mobile-topbar__hamburger {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 44px;
    height: 44px;
    border: none;
    background: transparent;
    color: var(--td-text-primary);
    cursor: pointer;
    border-radius: var(--td-radius-md);
  }

  .td-mobile-topbar__hamburger:hover {
    background: var(--td-surface-container-high);
  }

  .td-mobile-topbar__title {
    font-family: 'Manrope', system-ui, sans-serif;
    font-size: var(--td-font-lg);
    font-weight: 800;
    letter-spacing: -0.04em;
    color: var(--td-text-primary);
  }

  .td-content {
    padding: var(--td-space-4);
    overflow-x: hidden; /* mobile safeguard — prevents horizontal scroll from wide content */
  }
}

/* ─── Paper mode overrides ─── */
.td-shell--paper {
  background: var(--paper);
}

.td-shell--paper .td-content {
  background: var(--paper);
}

@media (max-width: 640px) {
  .td-shell--paper .td-mobile-topbar {
    background: var(--paper-2);
    border-bottom-color: var(--line);
  }

  .td-shell--paper .td-mobile-topbar__hamburger {
    color: var(--ink);
  }

  .td-shell--paper .td-mobile-topbar__hamburger:hover {
    background: var(--paper-card);
  }

  .td-shell--paper .td-mobile-topbar__title {
    font-family: var(--serif);
    font-weight: 500;
    color: var(--ink-deep);
  }
}

.td-shell--paper-phone .td-content {
  padding-bottom: calc(
    var(--td-space-4) + 56px + var(--paper-safe-bottom, env(safe-area-inset-bottom, 0px))
  );
}
</style>
