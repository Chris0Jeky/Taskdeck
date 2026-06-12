import { computed, onMounted, onScopeDispose, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { chatApi } from '../api/chatApi'
import { boardsApi } from '../api/boardsApi'
import { useToastStore } from '../store/toastStore'
import type { ChatProviderHealth, ChatSession } from '../types/chat'
import type { Board } from '../types/board'
import { normalizeChatRole } from '../utils/chat'
import { getErrorDisplay } from './useErrorMapper'
import { buildInputAssistOptions } from '../utils/inputAssist'
import type { InputAssistOption } from '../utils/inputAssist'
import { normalizeBoardIdQueryParam } from '../utils/navigation'

export function useAutomationChat() {
  const router = useRouter()
  const route = useRoute()
  const toast = useToastStore()

  const sessions = ref<ChatSession[]>([])
  const availableBoards = ref<Board[]>([])
  const selectedSession = ref<ChatSession | null>(null)
  const loadingSessions = ref(false)
  const loadingBoards = ref(false)
  const loadingHealth = ref(false)
  const creatingSession = ref(false)
  const sendingMessage = ref(false)
  let boardOptionsRequest: Promise<boolean> | null = null
  const chatHealth = ref<ChatProviderHealth | null>(null)
  const chatHealthLoadError = ref<string | null>(null)

  // Set once the owning scope is disposed; guards async continuations so
  // neither a resolved request nor an error racing teardown writes reactive
  // state after the scope is gone.
  let isDisposed = false

  const newSessionTitle = ref('')
  const newSessionBoardId = ref('')
  const selectedNewSessionBoardId = ref<string | null>(null)
  const messageContent = ref('')
  const requestProposal = ref(false)

  const boardOptions = computed(() =>
    buildInputAssistOptions(
      availableBoards.value.map((board) => ({
        value: board.id,
        label: board.name,
        helperText: board.isArchived
          ? 'Archived board'
          : (board.description?.trim() || 'Active board'),
        keywords: [board.description ?? '', board.name],
      })),
    ),
  )

  const boardNameById = computed(() => (
    new Map(availableBoards.value.map((board) => [board.id, board.name]))
  ))

  const boardById = computed(() => (
    new Map(availableBoards.value.map((board) => [board.id, board]))
  ))

  const sortedMessages = computed(() => {
    const current = selectedSession.value
    if (!current) {
      return []
    }
    return [...current.recentMessages].sort((a, b) => (
      Date.parse(a.createdAt) - Date.parse(b.createdAt)
    ))
  })

  const lastMessageIsClarification = computed(() => {
    const msgs = sortedMessages.value
    if (msgs.length === 0) return false
    const last = msgs[msgs.length - 1]
    return last.messageType === 'clarification' && normalizeChatRole(last.role) === 'Assistant'
  })

  const selectedSessionBoardName = computed(() => {
    const boardId = selectedSession.value?.boardId?.trim()
    if (!boardId) {
      return 'No board context'
    }
    return boardNameById.value.get(boardId) ?? 'Linked board context'
  })

  const pendingSessionBoardContextLabel = computed(() => {
    const selectedBoard = selectedNewSessionBoardId.value
      ? boardById.value.get(selectedNewSessionBoardId.value)
      : null
    if (selectedBoard) {
      return selectedBoard.name
    }
    if (newSessionBoardId.value.trim()) {
      return newSessionBoardId.value.trim()
    }
    return boardNameById.value.get(queryBoardId.value) ?? queryBoardId.value
  })

  const queryBoardId = computed(() => normalizeBoardIdQueryParam(route.query.boardId))

  function normalizeSelectedBoardId(rawValue: string): string | null {
    const trimmed = rawValue.trim()
    if (!trimmed) {
      return null
    }

    const selectedBoard = selectedNewSessionBoardId.value
      ? boardById.value.get(selectedNewSessionBoardId.value)
      : null
    if (selectedBoard) {
      const normalizedSelectedId = selectedBoard.id.trim().toLowerCase()
      const normalizedSelectedName = selectedBoard.name.trim().toLowerCase()
      const normalizedInput = trimmed.toLowerCase()
      if (normalizedInput === normalizedSelectedId || normalizedInput === normalizedSelectedName) {
        return selectedBoard.id
      }
    }

    const normalized = trimmed.toLowerCase()
    const byId = availableBoards.value.find((board) => board.id.toLowerCase() === normalized)
    if (byId) {
      return byId.id
    }

    const nameMatches = availableBoards.value.filter((board) => board.name.trim().toLowerCase() === normalized)
    return nameMatches.length === 1 ? nameMatches[0]!.id : null
  }

  function updateNewSessionBoardValue(value: string) {
    newSessionBoardId.value = value

    const selectedBoard = selectedNewSessionBoardId.value
      ? boardById.value.get(selectedNewSessionBoardId.value)
      : null
    if (!selectedBoard) {
      selectedNewSessionBoardId.value = null
      return
    }

    const normalizedValue = value.trim().toLowerCase()
    if (!normalizedValue) {
      selectedNewSessionBoardId.value = null
      return
    }

    const matchesSelectedBoard = normalizedValue === selectedBoard.id.trim().toLowerCase() ||
      normalizedValue === selectedBoard.name.trim().toLowerCase()
    if (!matchesSelectedBoard) {
      selectedNewSessionBoardId.value = null
    }
  }

  function handleNewSessionBoardSelect(option: InputAssistOption) {
    selectedNewSessionBoardId.value = option.value
    updateNewSessionBoardValue(option.label)
  }

  function applyRouteBoardContext() {
    if (!queryBoardId.value) {
      return
    }
    const matchedBoard = availableBoards.value.find((board) => board.id === queryBoardId.value)
    if (matchedBoard) {
      newSessionBoardId.value = matchedBoard.name
      selectedNewSessionBoardId.value = matchedBoard.id
    }
  }

  async function loadSessions() {
    try {
      loadingSessions.value = true
      const result = await chatApi.getMySessions()
      if (isDisposed) return
      sessions.value = result
      if (!selectedSession.value && sessions.value.length > 0) {
        await loadSession(sessions.value[0]!.id)
      }
    } catch (e: unknown) {
      if (isDisposed) return
      toast.error(getErrorDisplay(e, 'Failed to load chat sessions').message)
    } finally {
      if (!isDisposed) loadingSessions.value = false
    }
  }

  async function loadSession(sessionId: string) {
    try {
      const result = await chatApi.getSession(sessionId)
      if (isDisposed) return
      selectedSession.value = result
    } catch (e: unknown) {
      if (isDisposed) return
      toast.error(getErrorDisplay(e, 'Failed to load chat session').message)
    }
  }

  async function loadProviderHealth(options?: { probe?: boolean }) {
    try {
      loadingHealth.value = true
      chatHealthLoadError.value = null
      const result = await chatApi.getHealth(options)
      if (isDisposed) return
      chatHealth.value = result
    } catch (e: unknown) {
      if (isDisposed) return
      chatHealthLoadError.value = getErrorDisplay(e, 'Failed to load LLM status').message
      toast.error(chatHealthLoadError.value)
    } finally {
      if (!isDisposed) loadingHealth.value = false
    }
  }

  async function handleCreateSession() {
    if (!newSessionTitle.value.trim()) {
      toast.error('Session title is required')
      return
    }

    if (newSessionBoardId.value.trim()) {
      const didLoadBoards = await loadBoardOptions()
      if (!didLoadBoards) {
        return
      }
    }

    const normalizedBoardId = normalizeSelectedBoardId(newSessionBoardId.value)
    if (newSessionBoardId.value.trim() && !normalizedBoardId) {
      toast.error('Choose a board from the list or leave board context blank.')
      return
    }

    try {
      creatingSession.value = true
      const created = await chatApi.createSession({
        title: newSessionTitle.value.trim(),
        boardId: normalizedBoardId,
      })
      if (isDisposed) return
      newSessionTitle.value = ''
      newSessionBoardId.value = ''
      selectedNewSessionBoardId.value = null
      await loadSessions()
      if (isDisposed) return
      await loadSession(created.id)
    } catch (e: unknown) {
      if (isDisposed) return
      toast.error(getErrorDisplay(e, 'Failed to create session').message)
    } finally {
      if (!isDisposed) creatingSession.value = false
    }
  }

  async function sendMessageToSession(content: string) {
    if (!selectedSession.value) {
      toast.error('Select a session first')
      return
    }

    try {
      sendingMessage.value = true
      const sessionId = selectedSession.value.id
      await chatApi.sendMessage(sessionId, {
        content,
        requestProposal: requestProposal.value,
      })
      if (isDisposed) return
      messageContent.value = ''
      requestProposal.value = false
      await loadSession(sessionId)
    } catch (e: unknown) {
      if (isDisposed) return
      toast.error(getErrorDisplay(e, 'Failed to send message').message)
    } finally {
      if (!isDisposed) sendingMessage.value = false
    }
  }

  async function handleSendMessage() {
    if (!messageContent.value.trim()) {
      return
    }
    await sendMessageToSession(messageContent.value.trim())
  }

  async function handleSkipClarification() {
    await sendMessageToSession('Just do your best')
  }

  async function loadBoardOptions(): Promise<boolean> {
    if (boardOptionsRequest) {
      return await boardOptionsRequest
    }

    let request: Promise<boolean> | null = null
    request = (async () => {
      try {
        loadingBoards.value = true
        const result = await boardsApi.getBoards()
        if (isDisposed) return false
        availableBoards.value = result
        return true
      } catch (e: unknown) {
        if (isDisposed) return false
        toast.error(getErrorDisplay(e, 'Failed to load boards').message)
        return false
      } finally {
        if (!isDisposed) loadingBoards.value = false
        if (boardOptionsRequest === request) {
          boardOptionsRequest = null
        }
      }
    })()

    boardOptionsRequest = request
    return await boardOptionsRequest
  }

  function openRoute(path: string) {
    void router.push(path)
  }

  function resolveReviewBoardId(): string | null {
    const sessionBoardId = selectedSession.value?.boardId?.trim()
    if (sessionBoardId) {
      return sessionBoardId
    }
    return queryBoardId.value
  }

  function pushToReview(hash?: string) {
    const boardId = resolveReviewBoardId()
    void router.push({
      name: 'workspace-review',
      query: boardId ? { boardId } : undefined,
      hash,
    })
  }

  function applyHintSuggestion(example: string) {
    messageContent.value = example
    requestProposal.value = true
  }

  function openReviewRoute() {
    pushToReview()
  }

  function openProposalReview(proposalId: string) {
    pushToReview(`#proposal-${encodeURIComponent(proposalId)}`)
  }

  onMounted(() => {
    void loadSessions()
    void loadProviderHealth()
    void loadBoardOptions().then(() => {
      // Guard the continuation: loadBoardOptions can resolve after the owning
      // scope is disposed (e.g. navigation mid-flight), and applyRouteBoardContext
      // writes reactive state. Skip it once disposed to avoid a post-teardown write.
      if (isDisposed) return
      applyRouteBoardContext()
    })
  })

  const stopWatch = watch(
    () => [queryBoardId.value, availableBoards.value.length],
    () => {
      applyRouteBoardContext()
    },
  )

  onScopeDispose(() => {
    isDisposed = true
    stopWatch()
  })

  return {
    // State
    sessions,
    selectedSession,
    loadingSessions,
    loadingBoards,
    loadingHealth,
    creatingSession,
    sendingMessage,
    chatHealth,
    chatHealthLoadError,
    newSessionTitle,
    newSessionBoardId,
    messageContent,
    requestProposal,

    // Computed
    boardOptions,
    sortedMessages,
    lastMessageIsClarification,
    selectedSessionBoardName,
    pendingSessionBoardContextLabel,
    queryBoardId,

    // Methods
    updateNewSessionBoardValue,
    handleNewSessionBoardSelect,
    handleCreateSession,
    handleSendMessage,
    handleSkipClarification,
    loadBoardOptions,
    loadSession,
    loadProviderHealth,
    openRoute,
    applyHintSuggestion,
    openReviewRoute,
    openProposalReview,
  }
}
