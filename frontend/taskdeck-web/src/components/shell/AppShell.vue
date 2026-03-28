<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useSessionStore } from '../../store/sessionStore'
import { useWorkspaceStore } from '../../store/workspaceStore'
import { registerEscapeHandler } from '../../composables/useEscapeStack'
import CaptureModal from '../common/CaptureModal.vue'
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
      <ShellTopbar @open-command-palette="openCommandPalette" />

      <main class="td-content" role="main">
        <router-view />
      </main>
    </div>

    <ShellCommandPalette
      :visible="showCommandPalette"
      :items="commandItems"
      @close="closeCommandPalette"
      @activate="handleCommandActivate"
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
</style>
