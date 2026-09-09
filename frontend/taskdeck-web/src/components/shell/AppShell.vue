<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useSessionStore } from '../../store/sessionStore'
import { useFeatureFlagStore } from '../../store/featureFlagStore'
import { useWorkspaceStore } from '../../store/workspaceStore'
import { useCaptureStore } from '../../store/captureStore'
import { useBoardStore } from '../../store/boardStore'
import { usePaperThemeStore } from '../../store/paperThemeStore'
import { useWorkspaceLayoutStore } from '../../store/workspaceLayoutStore'
import WorkspaceExperienceSwitcher from '../workspace/WorkspaceExperienceSwitcher.vue'
import { useCaptureQueueSync } from '../../composables/useCaptureQueueSync'
import { registerEscapeHandler } from '../../composables/useEscapeStack'
import { useViewportMode } from '../../composables/useViewportMode'
import { provideShellKeyboardHelp } from '../../composables/useShellKeyboardHelp'
import {
  APP_SHELL_SHORTCUT_BINDINGS,
  shortcutBindingIsAvailable,
  strokeMatches,
  type AppShellShortcutAction,
  type AppShellShortcutBinding,
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
const route = useRoute()
const session = useSessionStore()
const featureFlags = useFeatureFlagStore()
const workspace = useWorkspaceStore()
const capture = useCaptureStore()
const board = useBoardStore()
const paperTheme = usePaperThemeStore()
const layout = useWorkspaceLayoutStore()
const focusedThinking = computed(() => route.name === 'workspace-thinking' && route.query.focus === '1')
const experienceTitles = { classic: 'Your workspace', studio: 'A little space to think.', companion: 'Your work, in good company.', unified: 'Everything, in its place.' }
const { mode: viewportMode } = useViewportMode()

const { pendingCount: captureQueuePending, syncing: captureQueueSyncing } = useCaptureQueueSync()

const sidebarRef = ref<SidebarRef | null>(null)
const isPaperNarrow = computed(() => paperTheme.isOn && viewportMode.value !== 'desktop')
const isPaperPhone = computed(() => paperTheme.isOn && viewportMode.value === 'phone')

const showCommandPalette = ref(false)
const showKeyboardHelp = ref(false)
const showCaptureModal = ref(false)

// Routed views cannot emit across `<router-view>`, so the one help surface this
// shell renders is reachable from inside them through this seam (#2007).
provideShellKeyboardHelp({
  open: () => {
    showKeyboardHelp.value = true
  },
})

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
      keywords: 'capture inbox quick note modal transcript',
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

/**
 * Only a surface that declares itself MODAL owns the keyboard (#1968).
 *
 * A bare `[role="dialog"]` is not enough, and matching it was a live defect:
 * `CardModal` keeps `role="dialog"` in both presentations but sets
 * `aria-modal` only outside the inspector, so the Paper desktop card inspector
 * -- a sticky side panel that traps nothing and leaves the board usable --
 * counted as a keyboard-owning surface. That made `?`, `mod+k` and
 * `mod+shift+c` dead for as long as a card was open for reading, and stopped
 * every non-Escape key pressed outside the panel.
 *
 * `dialog[open]` and `[role="alertdialog"]` stay: a native open `<dialog>` is
 * modal when shown as one and an alertdialog is modal by definition.
 */
const KEYBOARD_OWNING_SURFACE_SELECTOR = [
  'dialog[open]',
  '[role="alertdialog"]',
  '[aria-modal="true"]',
].join(', ')

function activeKeyboardOwningSurfaces(): HTMLElement[] {
  if (typeof document === 'undefined') return []

  return Array.from(document.querySelectorAll<HTMLElement>(KEYBOARD_OWNING_SURFACE_SELECTOR))
    .filter((surface) => {
      if (!surface.isConnected) return false
      if (surface.closest('[hidden], [aria-hidden="true"], [inert]')) return false

      const style = window.getComputedStyle(surface)
      return style.display !== 'none' && style.visibility !== 'hidden'
    })
}

/**
 * One surface scan per keydown, shared by the two guards below (#2636).
 *
 * The capture-phase listener always runs before the bubble-phase one for the
 * same event, so whichever asks first pays for the `querySelectorAll` plus
 * `getComputedStyle` sweep and the other reads the answer. Without the memo the
 * bubble guard would double the per-keystroke cost #1968 went out of its way to
 * make lazy.
 *
 * The answer is deliberately pinned to the event rather than re-read on the
 * bubble: a surface handler may have closed its own surface on the way up (a
 * palette option activating, say), and the key still belonged to the surface
 * that was open when it was pressed.
 *
 * Per shell instance, not per module: these live in this `setup()` closure, so
 * two shells (as tests mount them) never share a memo.
 */
let scannedEvent: KeyboardEvent | null = null
let scannedSurfaces: HTMLElement[] = []

function keyboardOwningSurfacesFor(event: KeyboardEvent): HTMLElement[] {
  if (scannedEvent !== event) {
    scannedEvent = event
    scannedSurfaces = activeKeyboardOwningSurfaces()
  }
  return scannedSurfaces
}

/**
 * True when this action's own surface is among the active ones and every active
 * surface belongs to the shell. That is what makes `?` and `mod+k` toggles
 * rather than one-way openers: the help dialog owns `?`, the command palette
 * owns `mod+k`, and neither opens over the other or over anything else (#1968).
 *
 * Deliberately not "the topmost surface owns it". Stack order is not readable
 * here: both help twins and both palettes teleport to `body`, and a `<Teleport>`
 * places its anchor when the SHELL mounts, not when the surface opens, so
 * document order is AppShell's template order whatever the user opened first.
 * Asking every surface instead would deadlock a stack -- open the help dialog,
 * then the topbar Search control, and neither key could close its own surface
 * again.
 *
 * `navigate` and `quick-capture` name no surface, so an active surface always
 * wins over them: nothing behind a modal should move the route, and quick
 * capture would stack a second modal on the first (the #1959 class).
 */
function shellSurfaceOwnsAction(surfaces: readonly HTMLElement[], action: AppShellShortcutAction): boolean {
  if (surfaces.length === 0) return true

  return surfaces.some((surface) => surface.dataset.shellSurface === action.type) &&
    surfaces.every((surface) => surface.dataset.shellSurface !== undefined)
}

const CHORD_TIMEOUT_MS = 1_000
let pendingChord: AppShellShortcutBinding | null = null
let chordTimer: ReturnType<typeof window.setTimeout> | null = null

function shortcutIsAvailable(binding: AppShellShortcutBinding): boolean {
  return shortcutBindingIsAvailable(binding, (flag) => featureFlags.isEnabled(flag))
}

function clearPendingChord() {
  pendingChord = null
  if (chordTimer !== null) {
    window.clearTimeout(chordTimer)
    chordTimer = null
  }
}

function consumeShortcut(event: KeyboardEvent) {
  event.preventDefault()
  // AppShell owns the workspace-level keys. Capture-phase handling means a
  // component-local listener for the same key can never also run, which is why
  // BoardView binds Left (not `h`) for previous-column navigation.
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

/**
 * Keep a key an active surface does not own from reaching the page behind it
 * (#2621).
 *
 * With the help dialog open over a Legacy board, a plain `f` or `n` used to
 * bubble past this listener to `BoardView`'s `useKeyboardShortcuts` window
 * listener: `f` toggled the filter panel behind the modal and `n` clicked the
 * column's add-card button and pulled focus out of the dialog.
 *
 * Two carve-outs keep this from taking more than it should:
 *   - Escape is never stopped. Board dialogs the escape stack does not carry
 *     (label manager, board settings, filter panel, column form) are closed by
 *     `BoardView.closeOpenUi` on the bubble, and stopping Escape here would
 *     strand them open.
 *   - A target inside the surface is left alone, because this listener runs in
 *     the capture phase, ahead of every handler the surface owns. Stopping
 *     there would break typing and arrow navigation inside modals.
 * Text-entry targets never reach this, and never triggered board shortcuts in
 * the first place -- `useKeyboardShortcuts` ignores them.
 */
function guardSurfaceFromPageShortcuts(event: KeyboardEvent, surfaces: readonly HTMLElement[]) {
  if (event.key === 'Escape') return

  const target = event.target instanceof Node ? event.target : null
  if (target && surfaces.some((surface) => surface.contains(target))) return

  event.stopPropagation()
}

/**
 * The other half of the same guard, for keys pressed from INSIDE the surface
 * (#2636).
 *
 * `guardSurfaceFromPageShortcuts` above runs in the capture phase and has to
 * stand aside when the target is inside the surface, because at that point the
 * surface's own handlers have not run yet. That carve-out was the leak PR #2635
 * recorded: Tab into the open help dialog on a Legacy board and press `f` or
 * `n`, and the event ran the dialog's handlers and then kept bubbling out to
 * `BoardView`'s `useKeyboardShortcuts` window listener -- the filter panel
 * toggled and the add-card composer pulled focus out of the dialog.
 *
 * This listener sits on `document` in the bubble phase. The invariant that
 * makes that safe is narrower than "the surfaces handle their own keys first",
 * so state it exactly:
 *
 *   A surface keeps a key only if it handles that key AT OR BELOW `document`
 *   -- an element-level handler, anywhere from the event target up to and
 *   including `document` -- or if the key is Escape, which is carved out below.
 *   Anything a surface binds on `window` is one hop OUTSIDE this guard and,
 *   unless it is Escape, will be silenced while a surface is active.
 *
 * Every surface in the tree satisfies that today by binding element-level
 * handlers: the palettes' `@keydown.down/up/enter`, `CaptureModal`'s
 * `@keydown`, the review dialogs' listeners on their own dialog elements. The
 * one surface that does NOT is `PaperShortcutsOverlay`, whose Escape handler is
 * a `window` bubble listener; it survives purely on the Escape carve-out, not
 * on the premise above. So a new `window`-level NON-Escape handler belonging to
 * a modal is a forbidden shape here -- it would be silenced -- and there is a
 * spec pinning that from the Paper help overlay.
 *
 * Note the reach: this is not scoped to the four shell surfaces. It fires for
 * every `dialog[open]`, `[role="alertdialog"]` or `[aria-modal="true"]` in the
 * app -- `CardModal`, `TdDialog` and the review dialogs built on it,
 * `ProvenanceDrawer`, `PaperBoardDialogShell`, the board modals,
 * `WorkspaceSetupModal`, `MfaChallengeModal` -- which is the point, since the
 * page-level listeners it has to silence all bind on `window`.
 *
 * Two carve-outs, for the same reasons the capture half has them:
 *   - Escape is never stopped. `useEscapeStack` listens in the capture phase so
 *     it is already past, but `BoardView.closeOpenUi` and
 *     `PaperShortcutsOverlay` both take Escape on the window bubble, and
 *     stopping it here would strand their surfaces open.
 *   - Text-entry targets are left alone. Typing is not a page shortcut:
 *     `useKeyboardShortcuts` ignores text entry outright, and honouring the
 *     early-out keeps the #1968 promise that an ordinary keystroke in a field
 *     never pays for the surface scan.
 */
function guardPageListenersFromSurfaceKeys(event: KeyboardEvent) {
  if (event.key === 'Escape') return
  if (event.isComposing) return
  if (isTextEntryTarget(event.target)) return
  if (keyboardOwningSurfacesFor(event).length === 0) return

  event.stopPropagation()
}

function handleKeydown(event: KeyboardEvent) {
  if (event.isComposing) {
    clearPendingChord()
    return
  }

  const textEntryTarget = isTextEntryTarget(event.target)

  // Scanned at most once per event, and only once something actually needs the
  // answer, so an ordinary keystroke typed into a field never pays for the
  // `querySelectorAll` plus `getComputedStyle` sweep (#1968). The memo is shared
  // with the bubble-phase guard so the pair still scans only once (#2636).
  const keyboardOwningSurfaces = () => keyboardOwningSurfacesFor(event)

  if (pendingChord) {
    const chord = pendingChord
    clearPendingChord()
    const nextStroke = chord.sequence[1]
    if (
      !textEntryTarget &&
      nextStroke &&
      strokeMatches(event, nextStroke) &&
      shortcutIsAvailable(chord) &&
      keyboardOwningSurfaces().length === 0
    ) {
      consumeShortcut(event)
      runAppShellShortcut(chord)
      return
    }
  }

  const direct = APP_SHELL_SHORTCUT_BINDINGS.find((binding) =>
    binding.sequence.length === 1 &&
    strokeMatches(event, binding.sequence[0]!) &&
    shortcutIsAvailable(binding) &&
    (!textEntryTarget || binding.allowInTextEntry === true) &&
    shellSurfaceOwnsAction(keyboardOwningSurfaces(), binding.action),
  )
  if (direct) {
    consumeShortcut(event)
    runAppShellShortcut(direct)
    return
  }

  if (textEntryTarget) return

  if (keyboardOwningSurfaces().length > 0) {
    guardSurfaceFromPageShortcuts(event, keyboardOwningSurfaces())
    return
  }

  const chord = APP_SHELL_SHORTCUT_BINDINGS.find((binding) =>
    binding.sequence.length > 1 &&
    shortcutIsAvailable(binding) &&
    strokeMatches(event, binding.sequence[0]!),
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
  // Bubble phase on `document`: after the surface's own handlers, before the
  // page-level `window` listeners (#2636).
  document.addEventListener('keydown', guardPageListenersFromSurfaceKeys)
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
      // The capture store's per-item generation guards are keyed by capture id
      // and belong to the session that recorded them (#2571).
      capture.resetForLogout()
      // Board list and detail state, and the reads still in flight for them,
      // belong to the account that was signed in (#1961).
      board.resetForLogout()
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
  document.removeEventListener('keydown', guardPageListenersFromSurfaceKeys)
  // Belt-and-braces. The memo lives in this instance's `setup()` closure, not at
  // module scope, so it is already unreachable once the instance is gone; this
  // just drops the last event and surface references at a known point.
  scannedEvent = null
  scannedSurfaces = []
})
</script>

<template>
  <div
    class="td-shell"
    :data-experience="layout.experience"
    :data-presentation="layout.presentation"
    :class="{
      'td-shell--paper': paperTheme.isOn,
      'td-shell--paper-phone': isPaperPhone,
      'td-shell--focus': focusedThinking,
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

      <div class="td-experience-bar">
        <div v-if="layout.experience !== 'classic'" class="td-experience-bar__identity">
          <span class="td-experience-bar__eyebrow">Taskdeck / {{ layout.experience }}</span>
          <span class="td-experience-bar__title">{{ experienceTitles[layout.experience] }}</span>
        </div>
        <WorkspaceExperienceSwitcher compact />
        <button v-if="layout.experience !== 'classic'" type="button" class="td-experience-capture" @click="openCaptureModal">+ Capture</button>
        <button v-if="layout.experience === 'studio'" type="button" class="td-experience-signout" @click="handleLogout">Sign out</button>
      </div>
      <nav v-if="layout.experience === 'studio'" class="td-studio-navigation" aria-label="Studio navigation">
        <button v-for="item in (sidebarRef?.availableNavItems ?? []).slice(0, 6)" :key="item.path" type="button" :aria-current="route.path === item.path ? 'page' : undefined" @click="router.push(item.path)">{{ item.label }}</button>
        <button type="button" @click="openCommandPalette">All destinations <span aria-hidden="true">↗</span></button>
      </nav>
      <div v-if="layout.experience === 'companion' && layout.presentation !== 'zen'" class="td-companion-context">
        <span class="td-companion-context__mark" aria-hidden="true">◌</span>
        <p><strong>Keep the thread.</strong> Capture a thought, review the proposed change, and carry it onto a board.</p>
        <button type="button" @click="openCommandPalette">Find your next step <span aria-hidden="true">→</span></button>
      </div>

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

<style scoped>
/* Keep the route and its draft mounted while clearing the surrounding navigation. */
.td-shell--focus :deep(.paper-sidebar),
.td-shell--focus :deep(.paper-bottombar),
.td-shell--focus :deep(.paper-topbar),
.td-shell--focus :deep(.td-sidebar),
.td-shell--focus :deep(.td-sidebar-overlay),
.td-shell--focus :deep(.td-topbar),
.td-shell--focus .td-mobile-topbar,
.td-shell--focus .td-experience-bar,
.td-shell--focus .td-studio-navigation,
.td-shell--focus .td-companion-context { display: none; }
.td-shell--focus .td-content { padding: clamp(16px, 4vw, 48px); }
</style>

<style scoped>
.td-experience-bar { display: flex; align-items: center; justify-content: flex-end; flex-wrap: wrap; gap: 20px; padding: 10px 28px; border-bottom: 1px solid var(--td-border-default); background: var(--td-surface-container-low); }
.td-experience-bar__identity { display: flex; flex-direction: column; gap: 6px; margin-right: auto; }
.td-experience-bar__eyebrow { text-transform: uppercase; letter-spacing: .15em; font: 10px var(--mono, monospace); color: var(--td-text-secondary); }
.td-experience-bar__title { font: 500 27px var(--serif, Georgia, serif); letter-spacing: -.04em; color: var(--td-text-primary); }
.td-experience-capture { min-height: 38px; padding: 8px 18px; border: 1px solid transparent; border-radius: 9px; background: var(--td-color-ember); color: var(--td-on-ember, #fff); font-weight: 600; }
.td-experience-signout { min-height: 36px; padding: 8px; background: transparent; border: 0; color: var(--td-text-secondary); font-size: 12px; cursor: pointer; }
.td-experience-signout:focus-visible, .td-experience-capture:focus-visible, .td-studio-navigation button:focus-visible, .td-companion-context button:focus-visible { outline: 2px solid var(--td-color-ember); outline-offset: 3px; }
.td-studio-navigation { display: flex; flex-wrap: wrap; gap: 8px; padding: 10px 32px; border-bottom: 1px solid var(--td-border-default); }
.td-studio-navigation button { padding: 9px 14px; border: 0; border-radius: 8px; background: transparent; color: var(--td-text-primary); font-size: 13px; cursor: pointer; }
.td-studio-navigation button:hover { background: var(--td-surface-container-high); }
.td-studio-navigation button[aria-current="page"] { background: var(--td-surface-container-high); color: var(--td-color-ember); box-shadow: inset 0 -2px var(--td-color-ember); }
.td-companion-context { display: flex; align-items: center; gap: 16px; margin: 20px 28px 0; padding: 14px 20px; border: 1px solid var(--td-border-default); border-radius: 14px; background: var(--td-surface-container-low); color: var(--td-text-secondary); font-size: 13px; }
.td-companion-context__mark { font: 36px var(--serif, serif); color: var(--td-color-ember); }
.td-companion-context p { flex: 1; margin: 0; line-height: 1.6; }
.td-companion-context strong { display: block; color: var(--td-text-primary); }
.td-companion-context button { background: transparent; color: var(--td-text-primary); border: 0; font-size: 12px; cursor: pointer; }
.td-shell[data-experience="studio"] .td-experience-bar { padding-block: 28px; }
.td-shell[data-experience="companion"] .td-content { padding-top: 24px; }
.td-shell[data-experience="unified"] .td-experience-bar { border-left: 3px solid var(--td-color-ember); }
.td-shell[data-presentation="zen"] .td-content { padding: 40px clamp(16px, 4vw, 72px); }
.td-shell[data-presentation="control"] .td-content { padding: 16px; }
.td-shell[data-presentation="zen"] .td-experience-bar__eyebrow { display: none; }
@media (min-width: 1024px) {
  .td-shell[data-experience="studio"] :deep(.paper-sidebar), .td-shell[data-experience="studio"] :deep(.td-sidebar) { display: none; }
  .td-shell[data-experience="studio"] :deep(.paper-topbar) { display: none; }
}
@media (max-width: 720px) {
  .td-experience-bar { padding: 12px 16px; gap: 12px; justify-content: flex-start; }
  .td-experience-bar__identity { width: 100%; }
  .td-experience-bar__title { font-size: 23px; }
  .td-studio-navigation { padding: 8px; gap: 2px; }
  .td-companion-context { margin: 12px 12px 0; padding: 12px; flex-wrap: wrap; }
  .td-shell[data-presentation="zen"] .td-content { padding: 20px 16px 80px; }
  .td-shell[data-presentation="control"] .td-content { padding-bottom: 80px; }
}
</style>
