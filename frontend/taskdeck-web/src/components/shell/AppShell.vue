<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useSessionStore } from '../../store/sessionStore'
import { useWorkspaceStore } from '../../store/workspaceStore'
import { registerEscapeHandler } from '../../composables/useEscapeStack'
import CaptureModal from '../common/CaptureModal.vue'
import OfflineBanner from './OfflineBanner.vue'
import SwUpdatePrompt from './SwUpdatePrompt.vue'
import ShellSidebar from './ShellSidebar.vue'
import ShellTopbar from './ShellTopbar.vue'
import ShellCommandPalette from './ShellCommandPalette.vue'
import ShellKeyboardHelp from './ShellKeyboardHelp.vue'
import type { CommandItem } from './ShellCommandPalette.vue'

const router = useRouter()
const session = useSessionStore()
const workspace = useWorkspaceStore()

const sidebarRef = ref<InstanceType<typeof ShellSidebar> | null>(null)

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
  return target instanceof HTMLInputElement ||
    target instanceof HTMLTextAreaElement ||
    target instanceof HTMLSelectElement ||
    (target instanceof HTMLElement && target.isContentEditable)
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
  window.addEventListener('keydown', handleKeydown)
  if (session.isAuthenticated && !workspace.hasHomeSummary && !workspace.homeLoading) {
    void workspace.fetchHomeSummary().catch(() => {})
  }
})

onUnmounted(() => {
  window.removeEventListener('keydown', handleKeydown)
})
</script>

<template>
  <div class="td-shell">
    <ShellSidebar
      ref="sidebarRef"
      :is-authenticated="session.isAuthenticated"
      @logout="handleLogout"
      @show-keyboard-help="showKeyboardHelp = true"
    />

    <div class="td-main-container">
      <OfflineBanner />
      <SwUpdatePrompt />
      <div class="td-mobile-topbar">
        <button
          class="td-mobile-topbar__hamburger"
          aria-label="Open navigation menu"
          @click="sidebarRef?.toggleMobileMenu()"
        >
          <span class="material-symbols-outlined">menu</span>
        </button>
        <span class="td-mobile-topbar__title">Taskdeck</span>
      </div>

      <ShellTopbar @open-command-palette="openCommandPalette" />

      <main id="td-main-content" class="td-content">
        <router-view />
      </main>
    </div>

    <ShellCommandPalette
      :visible="showCommandPalette"
      :items="commandItems"
      @close="closeCommandPalette"
      @activate="handleCommandActivate"
      @navigate-to-board="handleNavigateToBoard"
      @navigate-to-card="handleNavigateToCard"
    />

    <ShellKeyboardHelp
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
</style>
