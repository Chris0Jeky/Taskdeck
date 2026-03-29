import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuditStore } from '../store/auditStore'
import { useBoardStore } from '../store/boardStore'
import { useSessionStore } from '../store/sessionStore'
import { useToastStore } from '../store/toastStore'

export type ViewMode = 'board' | 'entity' | 'user'
export type DiscoverableEntityType = 'Board' | 'Column' | 'Card' | 'Label'

export interface SelectorOption {
  id: string
  label: string
  secondary?: string
}

export function formatTimestamp(ts: string): string {
  return new Date(ts).toLocaleString()
}

export function formatAction(action: string | number): string {
  if (typeof action === 'string') return action

  const map: Record<number, string> = {
    0: 'Created',
    1: 'Updated',
    2: 'Deleted',
    3: 'Archived',
    4: 'Unarchived',
    5: 'Moved',
    6: 'PermissionGranted',
    7: 'PermissionRevoked',
    8: 'OwnershipTransferred',
  }

  return map[action] ?? String(action)
}

export function normalizeEntityType(rawEntityType: string): DiscoverableEntityType | '' {
  const normalized = rawEntityType.trim().toLowerCase()

  if (normalized === 'board') return 'Board'
  if (normalized === 'column') return 'Column'
  if (normalized === 'card') return 'Card'
  if (normalized === 'label') return 'Label'

  return ''
}

export function useActivityQuery() {
  const route = useRoute()
  const router = useRouter()
  const audit = useAuditStore()
  const boards = useBoardStore()
  const session = useSessionStore()
  const toast = useToastStore()

  const viewMode = ref<ViewMode>('board')
  const selectedBoardId = ref('')
  const selectedEntityType = ref<DiscoverableEntityType | ''>('')
  const selectedEntityBoardId = ref('')
  const selectedEntityId = ref('')
  const limit = ref(50)

  const loadingEntitySource = ref(false)
  const suppressRouteSync = ref(false)
  const loadedEntityBoardId = ref<string | null>(null)
  const preserveRouteEntitySelection = ref(false)

  const boardOptions = computed<SelectorOption[]>(() => {
    return [...boards.boards]
      .sort((left, right) => left.name.localeCompare(right.name))
      .map((board) => ({
        id: board.id,
        label: board.isArchived ? `${board.name} (Archived)` : board.name,
      }))
  })

  const requiresEntityBoardContext = computed(() => {
    return selectedEntityType.value !== '' && selectedEntityType.value !== 'Board'
  })

  const entityOptions = computed<SelectorOption[]>(() => {
    if (!selectedEntityType.value) {
      return []
    }

    if (selectedEntityType.value === 'Board') {
      return boardOptions.value.map((board) => ({
        id: board.id,
        label: board.label,
      }))
    }

    if (!requiresEntityBoardContext.value || boards.currentBoard?.id !== selectedEntityBoardId.value) {
      return []
    }

    if (selectedEntityType.value === 'Column') {
      return [...boards.currentBoard.columns]
        .sort((left, right) => left.position - right.position)
        .map((column) => ({
          id: column.id,
          label: column.name,
        }))
    }

    if (selectedEntityType.value === 'Card') {
      const columnNames = new Map(boards.currentBoard.columns.map((column) => [column.id, column.name]))
      return [...boards.currentBoardCards]
        .sort((left, right) => left.position - right.position)
        .map((card) => ({
          id: card.id,
          label: card.title,
          secondary: columnNames.get(card.columnId),
        }))
    }

    return [...boards.currentBoardLabels]
      .sort((left, right) => left.name.localeCompare(right.name))
      .map((label) => ({
        id: label.id,
        label: label.name,
      }))
  })

  const canFetch = computed(() => {
    if (viewMode.value === 'board') {
      return selectedBoardId.value.length > 0
    }

    if (viewMode.value === 'entity') {
      if (!selectedEntityType.value || !selectedEntityId.value) {
        return false
      }

      if (requiresEntityBoardContext.value) {
        return selectedEntityBoardId.value.length > 0
      }

      return true
    }

    return true
  })

  const selectedIdForCopy = computed(() => {
    if (viewMode.value === 'board') {
      return selectedBoardId.value
    }

    if (viewMode.value === 'entity') {
      return selectedEntityId.value
    }

    return session.userId ?? ''
  })

  const selectedIdLabel = computed(() => {
    if (viewMode.value === 'board') {
      return 'Board ID'
    }

    if (viewMode.value === 'entity') {
      return `${selectedEntityType.value || 'Entity'} ID`
    }

    return 'User ID'
  })

  const emptyStateTitle = computed(() => {
    if (viewMode.value === 'board') {
      return 'No board activity yet'
    }

    if (viewMode.value === 'entity') {
      return 'No entity activity yet'
    }

    return 'No user activity yet'
  })

  const emptyStateBody = computed(() => {
    if (viewMode.value === 'board') {
      return 'Choose another board or open Review if you expected a pending change to appear here.'
    }

    if (viewMode.value === 'entity') {
      return 'Pick another entity or switch back to board history for a broader audit trail.'
    }

    return 'Activity will appear after you create, review, or update work in this workspace.'
  })

  function applySelectorDefaults() {
    if (viewMode.value === 'board') {
      if (!selectedBoardId.value && boardOptions.value.length > 0) {
        selectedBoardId.value = boardOptions.value[0]!.id
      }
      return
    }

    if (viewMode.value === 'entity') {
      if (!selectedEntityType.value) {
        selectedEntityType.value = 'Board'
      }

      if (selectedEntityType.value === 'Board') {
        if (!selectedEntityId.value && boardOptions.value.length > 0) {
          selectedEntityId.value = boardOptions.value[0]!.id
        }
        return
      }

      if (!selectedEntityBoardId.value && boardOptions.value.length > 0) {
        selectedEntityBoardId.value = boardOptions.value[0]!.id
      }
    }
  }

  async function ensureEntitySourceBoardLoaded() {
    if (!requiresEntityBoardContext.value || !selectedEntityBoardId.value) {
      return
    }

    if (loadedEntityBoardId.value === selectedEntityBoardId.value) {
      return
    }

    loadingEntitySource.value = true
    try {
      await boards.fetchBoard(selectedEntityBoardId.value)
      loadedEntityBoardId.value = selectedEntityBoardId.value
    } catch {
      // boardStore handles toast + error state.
    } finally {
      loadingEntitySource.value = false
    }
  }

  function syncFromRoute() {
    const routeBoardId = typeof route.params.boardId === 'string' ? route.params.boardId : ''
    const routeEntityType = typeof route.params.entityType === 'string' ? normalizeEntityType(route.params.entityType) : ''
    const routeEntityId = typeof route.params.entityId === 'string' ? route.params.entityId : ''

    if (routeBoardId) {
      preserveRouteEntitySelection.value = false
      viewMode.value = 'board'
      selectedBoardId.value = routeBoardId
      selectedEntityType.value = ''
      selectedEntityBoardId.value = ''
      selectedEntityId.value = ''
      return
    }

    if (routeEntityType && routeEntityId) {
      preserveRouteEntitySelection.value = true
      viewMode.value = 'entity'
      selectedEntityType.value = routeEntityType
      selectedEntityId.value = routeEntityId
      selectedBoardId.value = ''

      if (routeEntityType === 'Board') {
        selectedEntityBoardId.value = ''
      }
      return
    }

    if (route.name === 'workspace-activity-user') {
      preserveRouteEntitySelection.value = false
      viewMode.value = 'user'
      selectedBoardId.value = ''
      selectedEntityType.value = ''
      selectedEntityBoardId.value = ''
      selectedEntityId.value = ''
      return
    }

    preserveRouteEntitySelection.value = false
    viewMode.value = 'board'
    selectedBoardId.value = ''
    selectedEntityType.value = ''
    selectedEntityBoardId.value = ''
    selectedEntityId.value = ''
  }

  async function loadSelectorData() {
    try {
      await boards.fetchBoards(undefined, true)
    } catch {
      // boardStore handles toast + error state.
    }
  }

  async function fetchHistory() {
    if (viewMode.value === 'board' && selectedBoardId.value) {
      await audit.fetchBoardHistory(selectedBoardId.value, limit.value)
      return
    }

    if (viewMode.value === 'entity' && selectedEntityType.value && selectedEntityId.value) {
      await audit.fetchEntityHistory(selectedEntityType.value, selectedEntityId.value, limit.value)
      return
    }

    if (viewMode.value === 'user') {
      await audit.fetchUserHistory(limit.value)
    }
  }

  async function fetchHistorySafe() {
    try {
      await fetchHistory()
    } catch {
      // Store handles toast + error state.
    }
  }

  function routeForCurrentSelection() {
    if (viewMode.value === 'board' && selectedBoardId.value) {
      return {
        name: 'workspace-activity-board',
        params: { boardId: selectedBoardId.value },
      }
    }

    if (viewMode.value === 'entity' && selectedEntityType.value && selectedEntityId.value) {
      return {
        name: 'workspace-activity-entity',
        params: {
          entityType: selectedEntityType.value,
          entityId: selectedEntityId.value,
        },
      }
    }

    if (viewMode.value === 'user') {
      return { name: 'workspace-activity-user' }
    }

    return { name: 'workspace-activity' }
  }

  async function handleFetchClick() {
    if (!canFetch.value) {
      if (viewMode.value === 'board') {
        toast.error('Select a board to fetch activity history.')
        return
      }

      if (viewMode.value === 'entity') {
        toast.error('Select an entity type and item to fetch activity history.')
        return
      }
    }

    suppressRouteSync.value = true
    try {
      await router.push(routeForCurrentSelection())
      await fetchHistorySafe()
    } finally {
      suppressRouteSync.value = false
    }
  }

  async function copySelectedId() {
    const id = selectedIdForCopy.value
    if (!id) {
      return
    }

    if (!navigator.clipboard?.writeText) {
      toast.error('Clipboard is not available in this browser.')
      return
    }

    try {
      await navigator.clipboard.writeText(id)
      toast.success('Copied ID to clipboard')
    } catch {
      toast.error('Failed to copy ID')
    }
  }

  // Watchers
  watch(viewMode, async (mode) => {
    if (mode === 'board') {
      selectedEntityType.value = ''
      selectedEntityBoardId.value = ''
      selectedEntityId.value = ''
      applySelectorDefaults()
      return
    }

    if (mode === 'entity') {
      selectedBoardId.value = ''
      applySelectorDefaults()
      await ensureEntitySourceBoardLoaded()
      return
    }

    selectedBoardId.value = ''
    selectedEntityType.value = ''
    selectedEntityBoardId.value = ''
    selectedEntityId.value = ''
  })

  watch(selectedEntityType, async (nextType) => {
    selectedEntityId.value = ''

    if (!nextType) {
      selectedEntityBoardId.value = ''
      return
    }

    if (nextType === 'Board') {
      selectedEntityBoardId.value = ''
      applySelectorDefaults()
      return
    }

    applySelectorDefaults()
    loadedEntityBoardId.value = null
    await ensureEntitySourceBoardLoaded()
  })

  watch(selectedEntityBoardId, async () => {
    selectedEntityId.value = ''
    loadedEntityBoardId.value = null

    await ensureEntitySourceBoardLoaded()
  })

  watch(entityOptions, (options) => {
    if (viewMode.value !== 'entity') {
      return
    }

    if (options.length === 0) {
      if (preserveRouteEntitySelection.value && selectedEntityId.value) {
        return
      }

      selectedEntityId.value = ''
      return
    }

    const hasSelectedEntity = options.some((option) => option.id === selectedEntityId.value)
    if (!hasSelectedEntity) {
      if (preserveRouteEntitySelection.value && selectedEntityId.value) {
        return
      }

      selectedEntityId.value = options[0]!.id
    }
  })

  watch(boardOptions, () => {
    applySelectorDefaults()
  })

  watch(
    () => route.fullPath,
    async () => {
      if (suppressRouteSync.value) {
        return
      }

      syncFromRoute()
      applySelectorDefaults()
      await ensureEntitySourceBoardLoaded()
      await fetchHistorySafe()
      preserveRouteEntitySelection.value = false
    }
  )

  async function initialize() {
    await loadSelectorData()
    syncFromRoute()
    applySelectorDefaults()
    await ensureEntitySourceBoardLoaded()
    await fetchHistorySafe()
    preserveRouteEntitySelection.value = false
  }

  return {
    // State
    viewMode,
    selectedBoardId,
    selectedEntityType,
    selectedEntityBoardId,
    selectedEntityId,
    limit,
    loadingEntitySource,

    // Computed
    boardOptions,
    requiresEntityBoardContext,
    entityOptions,
    canFetch,
    selectedIdForCopy,
    selectedIdLabel,
    emptyStateTitle,
    emptyStateBody,

    // Actions
    handleFetchClick,
    copySelectedId,
    initialize,
  }
}
