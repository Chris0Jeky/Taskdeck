import { computed, onMounted, ref, watch } from 'vue'
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
      new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime()
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
      sessions.value = await chatApi.getMySessions()
      if (!selectedSession.value && sessions.value.length > 0) {
        await loadSession(sessions.value[0]!.id)
      }
    } catch (e: unknown) {
      toast.error(getErrorDisplay(e, 'Failed to load chat sessions').message)
    } finally {
      loadingSessions.value = false
    }
  }

  async function loadSession(sessionId: string) {
    try {
      selectedSession.value = await chatApi.getSession(sessionId)
    } catch (e: unknown) {
      toast.error(getErrorDisplay(e, 'Failed to load chat session').message)
    }
  }

  async function loadProviderHealth(options?: { probe?: boolean }) {
    try {
      loadingHealth.value = true
      chatHealthLoadError.value = null
      chatHealth.value = await chatApi.getHealth(options)
    } catch (e: unknown) {
      chatHealthLoadError.value = getErrorDisplay(e, 'Failed to load LLM status').message
      toast.error(chatHealthLoadError.value)
    } finally {
      loadingHealth.value = false
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
      newSessionTitle.value = ''
      newSessionBoardId.value = ''
      selectedNewSessionBoardId.value = null
      await loadSessions()
      await loadSession(created.id)
    } catch (e: unknown) {
      toast.error(getErrorDisplay(e, 'Failed to create session').message)
    } finally {
      creatingSession.value = false
    }
  }

  async function handleSendMessage() {
    if (!selectedSession.value) {
      toast.error('Select a session first')
      return
    }
    if (!messageContent.value.trim()) {
      return
    }

    try {
      sendingMessage.value = true
      await chatApi.sendMessage(selectedSession.value.id, {
        content: messageContent.value.trim(),
        requestProposal: requestProposal.value,
      })
      messageContent.value = ''
      requestProposal.value = false
      await loadSession(selectedSession.value.id)
    } catch (e: unknown) {
      toast.error(getErrorDisplay(e, 'Failed to send message').message)
    } finally {
      sendingMessage.value = false
    }
  }

  async function handleSkipClarification() {
    if (!selectedSession.value) return
    try {
      sendingMessage.value = true
      await chatApi.sendMessage(selectedSession.value.id, {
        content: 'Just do your best',
        requestProposal: requestProposal.value,
      })
      messageContent.value = ''
      requestProposal.value = false
      await loadSession(selectedSession.value.id)
    } catch (e: unknown) {
      toast.error(getErrorDisplay(e, 'Failed to send message').message)
    } finally {
      sendingMessage.value = false
    }
  }

  async function loadBoardOptions(): Promise<boolean> {
    if (boardOptionsRequest) {
      return await boardOptionsRequest
    }

    let request: Promise<boolean> | null = null
    request = (async () => {
      try {
        loadingBoards.value = true
        availableBoards.value = await boardsApi.getBoards()
        return true
      } catch (e: unknown) {
        toast.error(getErrorDisplay(e, 'Failed to load boards').message)
        return false
      } finally {
        loadingBoards.value = false
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
      applyRouteBoardContext()
    })
  })

  watch(
    () => [queryBoardId.value, availableBoards.value.length],
    () => {
      applyRouteBoardContext()
    },
  )

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
