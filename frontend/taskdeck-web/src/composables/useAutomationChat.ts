import { computed, onMounted, onScopeDispose, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { chatApi } from '../api/chatApi'
import { boardsApi } from '../api/boardsApi'
import { useToastStore } from '../store/toastStore'
import type { ChatContextSelection, ChatMessage, ChatProviderHealth, ChatSession } from '../types/chat'
import type { Board } from '../types/board'
import { normalizeChatRole } from '../utils/chat'
import { getErrorDisplay } from './useErrorMapper'
import { buildInputAssistOptions } from '../utils/inputAssist'
import type { InputAssistOption } from '../utils/inputAssist'
import { normalizeBoardIdQueryParam } from '../utils/navigation'

export function useAutomationChat(options: { boardId?: () => string | undefined; sendBlocked?: () => boolean } = {}) {
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
  const refreshingReceipt = ref(false)
  const receiptRefreshError = ref<string | null>(null)
  let receiptGeneration = 0
  const bindingBoard = ref(false)
  const bindingMessageId = ref<string | null>(null)
  const boardBindingError = ref<string | null>(null)
  const boardBindingReceipt = ref<string | null>(null)
  const boardOptionsLoadError = ref<string | null>(null)
  let boardOptionsRequest: Promise<boolean> | null = null
  let sessionSelectionGeneration = 0
  let boardBindingGeneration = 0
  let requestedSessionId: string | null = null
  let localMessageSequence = 0
  const localMessagesBySession = new Map<string, ChatMessage[]>()
  const sessionWriteGenerations = new Map<string, number>()
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
  const contextSelection = ref<ChatContextSelection | null>(null)
  watch([() => selectedSession.value?.id, () => selectedSession.value?.boardId], () => {
    contextSelection.value = null
    receiptGeneration++
    refreshingReceipt.value = false
    receiptRefreshError.value = null
  }, { flush: 'sync' })

  const eligibleBoards = computed(() => availableBoards.value.filter((board) => (
    !board.isArchived && board.canWrite !== false
  )))

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

  const pendingBoardRecovery = computed(() => {
    const messages = sortedMessages.value
    for (let index = messages.length - 1; index >= 0; index--) {
      const message = messages[index]!
      if (normalizeChatRole(message.role) !== 'Assistant') continue
      const needsBoardRecovery = message.messageType === 'action-needs-board'
        || (message.messageType === 'clarification'
          && message.content.includes('Select a writable board below'))
      if (!needsBoardRecovery) return null

      for (let userIndex = index - 1; userIndex >= 0; userIndex--) {
        const userMessage = messages[userIndex]!
        if (normalizeChatRole(userMessage.role) === 'User') {
          return {
            messageId: message.id,
            instruction: userMessage.content,
          }
        }
      }
      return null
    }
    return null
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

  const queryBoardId = computed(() => normalizeBoardIdQueryParam(options.boardId?.() ?? route.query.boardId))

  function createLocalUserMessage(sessionId: string, content: string, assistantCreatedAt: string): ChatMessage {
    const assistantTimestamp = Date.parse(assistantCreatedAt)
    const createdAt = Number.isFinite(assistantTimestamp)
      ? new Date(assistantTimestamp - 1).toISOString()
      : new Date().toISOString()

    // Local-only identity: this message is merged into the visible transcript,
    // never sent back through the chat API. The sequence keeps IDs unique within
    // this composable while the timestamp fixes the user/reply ordering.
    localMessageSequence += 1
    return {
      id: `local-user-${sessionId}-${localMessageSequence}`,
      sessionId,
      role: 'User',
      content,
      messageType: 'text',
      proposalId: null,
      tokenUsage: null,
      createdAt,
    }
  }

  function retainLocalMessages(sessionId: string, messages: ChatMessage[]): ChatMessage[] {
    const existing = localMessagesBySession.get(sessionId) ?? []
    const byId = new Map(existing.map((message) => [message.id, message]))
    for (const message of messages) {
      byId.set(message.id, message)
    }
    const retained = [...byId.values()]
    localMessagesBySession.set(sessionId, retained)
    return retained
  }

  function mergeLocalMessages(messages: ChatMessage[], localMessages: ChatMessage[]): ChatMessage[] {
    const knownIds = new Set(messages.map((message) => message.id))
    return [
      ...messages,
      ...localMessages.filter((message) => !knownIds.has(message.id)),
    ]
  }

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
      sessions.value = options.boardId?.() ? result.filter(session => session.boardId === options.boardId!()) : result
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
    requestedSessionId = sessionId
    const selectionGeneration = ++sessionSelectionGeneration
    boardBindingError.value = null
    boardBindingReceipt.value = null
    try {
      const result = await chatApi.getSession(sessionId)
      if (isDisposed || selectionGeneration !== sessionSelectionGeneration) return
      if (options.boardId?.() && result.boardId !== options.boardId()) throw new Error('This conversation belongs to a different board.')
      localMessagesBySession.delete(sessionId)
      sessionWriteGenerations.set(sessionId, (sessionWriteGenerations.get(sessionId) ?? 0) + 1)
      selectedSession.value = result
    } catch (e: unknown) {
      if (isDisposed || selectionGeneration !== sessionSelectionGeneration) return
      requestedSessionId = selectedSession.value?.id ?? null
      toast.error(getErrorDisplay(e, 'Failed to load chat session').message)
    }
  }

  async function refreshSelectedSession(sessionId: string) {
    const generation = ++receiptGeneration
    const selectionGeneration = sessionSelectionGeneration
    const writeGeneration = sessionWriteGenerations.get(sessionId) ?? 0
    const isCurrent = () => !isDisposed && generation === receiptGeneration
      && selectionGeneration === sessionSelectionGeneration
      && requestedSessionId === sessionId && selectedSession.value?.id === sessionId
      && writeGeneration === (sessionWriteGenerations.get(sessionId) ?? 0)
    refreshingReceipt.value = true
    receiptRefreshError.value = null
    try {
      // The send already succeeded and its messages are retained locally. A
      // failed reconciliation must not hold continuation behind read retries.
      const result = await chatApi.getSession(sessionId, { skipRetry: true })
      if (!isCurrent()) return
      if (options.boardId?.() && result.boardId !== options.boardId()) throw new Error('This conversation belongs to a different board.')
      localMessagesBySession.delete(sessionId)
      sessionWriteGenerations.set(sessionId, (sessionWriteGenerations.get(sessionId) ?? 0) + 1)
      selectedSession.value = result
      const sessionIndex = sessions.value.findIndex((session) => session.id === sessionId)
      if (sessionIndex >= 0) sessions.value.splice(sessionIndex, 1, result)
    } catch (e: unknown) {
      if (!isCurrent()) return
      receiptRefreshError.value = 'Your message was sent, but its saved conversation and source receipts could not be refreshed. Retry the refresh; do not resend the message.'
      toast.error(getErrorDisplay(e, 'Failed to load chat session').message)
    } finally {
      if (!isDisposed && generation === receiptGeneration) refreshingReceipt.value = false
    }
  }

  async function retryReceiptRefresh() {
    if (!selectedSession.value || sendingMessage.value || refreshingReceipt.value) return
    await refreshSelectedSession(selectedSession.value.id)
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

    if (isDisposed) return

    const normalizedBoardId = options.boardId?.() ?? normalizeSelectedBoardId(newSessionBoardId.value)
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

    if (sendingMessage.value || refreshingReceipt.value || options.sendBlocked?.()) return

    const sessionId = selectedSession.value.id
    try {
      sendingMessage.value = true
      const context = contextSelection.value
      const sentMessage = await chatApi.sendMessage(sessionId, { content, ...(context ? { context } : {}) })
      if (isDisposed) return
      if (requestedSessionId === sessionId && selectedSession.value?.id === sessionId) {
        messageContent.value = ''
        const currentSession = selectedSession.value
        const localUserMessage = createLocalUserMessage(sessionId, content, sentMessage.createdAt)
        sessionWriteGenerations.set(sessionId, (sessionWriteGenerations.get(sessionId) ?? 0) + 1)
        const retainedLocalMessages = retainLocalMessages(sessionId, [localUserMessage, sentMessage])
        selectedSession.value = {
          ...currentSession,
          recentMessages: mergeLocalMessages(currentSession.recentMessages, retainedLocalMessages),
        }
        await refreshSelectedSession(sessionId)
      }
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

  async function bindBoardToPendingTurn(messageId: string, boardId: string) {
    const session = selectedSession.value
    const pending = pendingBoardRecovery.value
    if (!session || !pending || pending.messageId !== messageId || session.boardId) return
    if (!eligibleBoards.value.some((board) => board.id === boardId)) {
      boardBindingError.value = 'Choose an active board you can edit.'
      return
    }

    const sessionId = session.id
    const bindingGeneration = ++boardBindingGeneration
    const writeGenerationAtBindingStart = sessionWriteGenerations.get(sessionId) ?? 0
    const localMessageIdsAtBindingStart = new Set(
      (localMessagesBySession.get(sessionId) ?? []).map((message) => message.id),
    )
    bindingBoard.value = true
    bindingMessageId.value = messageId
    boardBindingError.value = null
    try {
      const bound = await chatApi.bindBoard(sessionId, { boardId })
      if (isDisposed) return

      let effectiveBound = bound
      if (
        requestedSessionId === sessionId
        && selectedSession.value?.id === sessionId
        && bindingGeneration === boardBindingGeneration
      ) {
        const currentSession = selectedSession.value
        const writeGeneration = sessionWriteGenerations.get(sessionId) ?? 0
        if (writeGeneration === writeGenerationAtBindingStart) {
          localMessagesBySession.delete(sessionId)
          selectedSession.value = bound
        } else {
          const newerLocalMessages = (localMessagesBySession.get(sessionId) ?? [])
            .filter((message) => !localMessageIdsAtBindingStart.has(message.id))
          const currentMessages = currentSession.recentMessages
            .filter((message) => !localMessageIdsAtBindingStart.has(message.id))
          effectiveBound = {
            ...currentSession,
            boardId: bound.boardId,
            recentMessages: mergeLocalMessages(
              mergeLocalMessages(bound.recentMessages, currentMessages),
              newerLocalMessages,
            ),
          }
          localMessagesBySession.set(sessionId, newerLocalMessages)
          selectedSession.value = effectiveBound
        }
        boardBindingReceipt.value = boardNameById.value.get(boardId) ?? 'the selected board'
      }

      const sessionIndex = sessions.value.findIndex((item) => item.id === sessionId)
      if (sessionIndex >= 0) sessions.value.splice(sessionIndex, 1, effectiveBound)
    } catch (e: unknown) {
      if (isDisposed || requestedSessionId !== sessionId || selectedSession.value?.id !== sessionId) return
      boardBindingError.value = getErrorDisplay(e, 'Failed to link board').message
    } finally {
      if (!isDisposed && bindingGeneration === boardBindingGeneration) {
        bindingBoard.value = false
        bindingMessageId.value = null
      }
    }
  }

  async function continuePendingInstruction(messageId: string) {
    const pending = pendingBoardRecovery.value
    const session = selectedSession.value
    if (!pending || pending.messageId !== messageId || !session?.boardId) return
    await sendMessageToSession(pending.instruction)
  }

  async function loadBoardOptions(): Promise<boolean> {
    if (boardOptionsRequest) {
      return await boardOptionsRequest
    }

    let request: Promise<boolean> | null = null
    request = (async () => {
      try {
        loadingBoards.value = true
        boardOptionsLoadError.value = null
        const result = await boardsApi.getBoards()
        if (isDisposed) return false
        availableBoards.value = result
        return true
      } catch (e: unknown) {
        if (isDisposed) return false
        boardOptionsLoadError.value = getErrorDisplay(e, 'Failed to load boards').message
        toast.error(boardOptionsLoadError.value)
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
    localMessagesBySession.clear()
    sessionWriteGenerations.clear()
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
    refreshingReceipt,
    receiptRefreshError,
    bindingBoard,
    bindingMessageId,
    boardBindingError,
    boardBindingReceipt,
    boardOptionsLoadError,
    chatHealth,
    chatHealthLoadError,
    newSessionTitle,
    newSessionBoardId,
    messageContent,
    contextSelection,

    // Computed
    boardOptions,
    eligibleBoards,
    sortedMessages,
    lastMessageIsClarification,
    pendingBoardRecovery,
    selectedSessionBoardName,
    pendingSessionBoardContextLabel,
    queryBoardId,

    // Methods
    updateNewSessionBoardValue,
    handleNewSessionBoardSelect,
    handleCreateSession,
    handleSendMessage,
    retryReceiptRefresh,
    handleSkipClarification,
    bindBoardToPendingTurn,
    continuePendingInstruction,
    loadBoardOptions,
    loadSession,
    loadProviderHealth,
    openRoute,
    applyHintSuggestion,
    openReviewRoute,
    openProposalReview,
  }
}
