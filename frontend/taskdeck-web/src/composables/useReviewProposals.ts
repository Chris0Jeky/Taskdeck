import { computed, nextTick, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { automationApi } from '../api/automationApi'
import { boardsApi } from '../api/boardsApi'
import type { ReviewSummaryCard } from '../components/review/ReviewSummaryCards.vue'
import { useToastStore } from '../store/toastStore'
import {
  normalizeProposalSourceType,
  normalizeProposalStatus,
} from '../utils/automation'
import { buildInputAssistOptions } from '../utils/inputAssist'
import { normalizeBoardIdQueryParam } from '../utils/navigation'
import type { Proposal as ApiProposal } from '../types/automation'
import type { Board } from '../types/board'
import { getErrorDisplay } from './useErrorMapper'
import { usePerformanceMark } from './usePerformanceMark'

export function useReviewProposals() {
  const route = useRoute()
  const router = useRouter()
  const toast = useToastStore()
  const reviewLoadPerf = usePerformanceMark('review-load')

  const proposals = ref<ApiProposal[]>([])
  const proposalsLoading = ref(false)
  let latestProposalLoadRequestId = 0
  const availableBoards = ref<Board[]>([])
  const loadingBoards = ref(false)
  const boardFilterInput = ref('')
  const activeBoardFilter = computed(() => normalizeBoardIdQueryParam(route.query.boardId))
  const showCompleted = ref(false)

  // Reactive clock for client-side expiry detection -- updates every 60 s
  const nowMs = ref(Date.now())
  let clockInterval: ReturnType<typeof setInterval> | null = null

  function startClock() {
    clockInterval = setInterval(() => {
      nowMs.value = Date.now()
    }, 60_000)
  }

  function stopClock() {
    if (clockInterval !== null) {
      clearInterval(clockInterval)
      clockInterval = null
    }
  }

  const completedStatuses = new Set(['Applied', 'Rejected', 'Failed', 'Expired', 'Dismissed'])

  const boardOptions = computed(() =>
    buildInputAssistOptions(
      availableBoards.value.map((board) => ({
        value: board.id,
        label: board.name,
      })),
    ),
  )

  const activeBoardName = computed(() => {
    if (!activeBoardFilter.value) return ''
    const normalizedActiveId = normalizeBoardIdQueryParam(activeBoardFilter.value).toLowerCase()
    const board = availableBoards.value.find(
      (b) => normalizeBoardIdQueryParam(b.id).toLowerCase() === normalizedActiveId,
    )
    return board?.name ?? activeBoardFilter.value
  })

  function matchesActiveBoardFilter(boardId: string | null | undefined): boolean {
    if (!activeBoardFilter.value) return true
    const normalizedBoardId = normalizeBoardIdQueryParam(boardId).toLowerCase()
    return normalizedBoardId === activeBoardFilter.value.toLowerCase()
  }

  function isProposalExpired(proposal: ApiProposal): boolean {
    const normalized = normalizeProposalStatus(proposal.status)
    if (normalized === 'Expired') return true
    if (normalized === 'PendingReview' || normalized === 'Approved') {
      return new Date(proposal.expiresAt).getTime() <= nowMs.value
    }
    return false
  }

  const visibleProposals = computed(() =>
    proposals.value.filter((proposal) => {
      if (!matchesActiveBoardFilter(proposal.boardId)) return false
      const status = normalizeProposalStatus(proposal.status)
      if (status === 'Dismissed') return false
      if (isProposalExpired(proposal)) return true
      if (!showCompleted.value && completedStatuses.has(status)) return false
      return true
    }),
  )

  function captureSourceReference(proposal: ApiProposal): string | null {
    if (normalizeProposalSourceType(proposal.sourceType) !== 'Queue') return null
    if (!proposal.sourceReferenceId) return null
    const trimmed = proposal.sourceReferenceId.trim()
    return trimmed.length > 0 ? trimmed : null
  }

  function hasProvenanceContext(proposal: ApiProposal): boolean {
    return !!captureSourceReference(proposal)
  }

  const summaryCards = computed<ReviewSummaryCard[]>(() => {
    let pendingReview = 0
    let readyToExecute = 0
    let captureLinked = 0
    let appliedRecently = 0

    for (const proposal of visibleProposals.value) {
      const normalizedStatus = normalizeProposalStatus(proposal.status)
      const expired = isProposalExpired(proposal)

      if (normalizedStatus === 'PendingReview' && !expired) pendingReview += 1
      else if (normalizedStatus === 'Approved' && !expired) readyToExecute += 1
      else if (normalizedStatus === 'Applied') appliedRecently += 1

      if (hasProvenanceContext(proposal)) captureLinked += 1
    }

    return [
      { id: 'pending-review', label: 'Pending review', value: pendingReview, helper: 'Changes waiting for an explicit decision.' },
      { id: 'ready-to-execute', label: 'Ready to execute', value: readyToExecute, helper: 'Approved proposals that can now land on boards.' },
      { id: 'capture-linked', label: 'Capture-linked', value: captureLinked, helper: 'Review items that came through the inbox loop.' },
      { id: 'applied', label: 'Applied', value: appliedRecently, helper: 'Proposals already executed successfully.' },
    ]
  })

  function isProposalDismissable(proposal: ApiProposal): boolean {
    const status = normalizeProposalStatus(proposal.status)
    return status === 'Applied' || status === 'Rejected' || status === 'Failed' || status === 'Expired'
  }

  const dismissableProposalIds = computed(() =>
    proposals.value
      .filter((p) => isProposalDismissable(p))
      .filter((p) => matchesActiveBoardFilter(p.boardId))
      .map((p) => p.id),
  )

  // --- Data loading ---

  function getProposalIdFromHash(hash: string): string | null {
    if (!hash.startsWith('#proposal-')) return null
    const rawId = hash.slice('#proposal-'.length).trim()
    if (!rawId) return null
    try {
      return decodeURIComponent(rawId)
    } catch {
      return null
    }
  }

  async function scrollToProposalFromHash() {
    const proposalId = getProposalIdFromHash(route.hash)
    if (!proposalId) return
    await nextTick()
    const element = document.getElementById(`proposal-${proposalId}`)
    element?.scrollIntoView({ block: 'nearest' })
  }

  function upsertProposal(proposal: ApiProposal) {
    const existingIndex = proposals.value.findIndex((current) => current.id === proposal.id)
    if (existingIndex >= 0) {
      proposals.value[existingIndex] = proposal
      return
    }
    const proposalCreatedAt = new Date(proposal.createdAt).getTime()
    const insertIndex = proposals.value.findIndex((current) => new Date(current.createdAt).getTime() < proposalCreatedAt)
    if (insertIndex >= 0) {
      proposals.value.splice(insertIndex, 0, proposal)
      return
    }
    proposals.value.push(proposal)
  }

  function isHttpNotFound(error: unknown): boolean {
    const candidate = error as { response?: { status?: number } } | null
    return candidate?.response?.status === 404
  }

  async function openProposalFromHash() {
    if (proposalsLoading.value) return
    const proposalId = getProposalIdFromHash(route.hash)
    if (!proposalId) return

    const currentProposal = proposals.value.find((p) => p.id === proposalId)
    if (currentProposal) {
      if (!matchesActiveBoardFilter(currentProposal.boardId)) {
        await router.replace({ name: 'workspace-review', query: route.query })
        return
      }
      await scrollToProposalFromHash()
      return
    }

    try {
      const fetchedProposal = await automationApi.getProposal(proposalId)
      if (getProposalIdFromHash(route.hash) !== proposalId) return
      if (!matchesActiveBoardFilter(fetchedProposal.boardId)) {
        await router.replace({ name: 'workspace-review', query: route.query })
        return
      }
      upsertProposal(fetchedProposal)
      await nextTick()
      await scrollToProposalFromHash()
    } catch (e: unknown) {
      if (getProposalIdFromHash(route.hash) !== proposalId) return
      if (isHttpNotFound(e)) {
        await router.replace({ name: 'workspace-review', query: route.query })
        return
      }
      toast.error(getErrorDisplay(e, 'Failed to load proposal').message)
    }
  }

  async function loadProposals() {
    reviewLoadPerf.start()
    const requestId = ++latestProposalLoadRequestId

    try {
      proposalsLoading.value = true
      const loadedProposals = await automationApi.getProposals({
        limit: 200,
        boardId: activeBoardFilter.value || undefined,
      })
      if (requestId !== latestProposalLoadRequestId) return
      proposals.value = loadedProposals
    } catch (e: unknown) {
      if (requestId !== latestProposalLoadRequestId) return
      toast.error(getErrorDisplay(e, 'Failed to load proposals').message)
    } finally {
      if (requestId === latestProposalLoadRequestId) proposalsLoading.value = false
      reviewLoadPerf.end()
    }

    if (requestId === latestProposalLoadRequestId) {
      await openProposalFromHash()
    }
  }

  async function loadBoardOptions() {
    try {
      loadingBoards.value = true
      availableBoards.value = await boardsApi.getBoards(undefined, true)
    } catch {
      // Board options are non-critical
    } finally {
      loadingBoards.value = false
    }
  }

  // --- Navigation helpers ---

  function inboxPath(boardId?: string | null, captureItemId?: string): string {
    const encodedBoardId = boardId ? encodeURIComponent(boardId) : null
    const query = encodedBoardId ? `?boardId=${encodedBoardId}` : ''
    const hash = captureItemId ? `#capture-${encodeURIComponent(captureItemId)}` : ''
    return `/workspace/inbox${query}${hash}`
  }

  function openInbox() {
    void router.push(inboxPath(activeBoardFilter.value))
  }

  function proposalHref(proposal: ApiProposal): string {
    const query = proposal.boardId ?? activeBoardFilter.value
    const encodedProposalId = encodeURIComponent(proposal.id)
    return query
      ? `/workspace/review?boardId=${encodeURIComponent(query)}#proposal-${encodedProposalId}`
      : `/workspace/review#proposal-${encodedProposalId}`
  }

  function captureHrefForProposal(proposal: ApiProposal): string {
    const sourceReference = captureSourceReference(proposal)
    return sourceReference
      ? inboxPath(proposal.boardId ?? activeBoardFilter.value, sourceReference)
      : inboxPath(activeBoardFilter.value)
  }

  function openRoute(path: string) {
    void router.push(path)
  }

  function openBoard(boardId: string) {
    void router.push(`/workspace/boards/${boardId}`)
  }

  function applyBoardFilter(boardId: string) {
    const trimmed = boardId.trim()
    boardFilterInput.value = ''
    if (trimmed) {
      void router.push({ name: 'workspace-review', query: { boardId: trimmed } })
    } else {
      void router.push({ name: 'workspace-review' })
    }
  }

  function clearBoardFilter() {
    boardFilterInput.value = ''
    void router.push({ name: 'workspace-review' })
  }

  // --- Watchers ---

  watch(
    () => route.hash,
    () => { void openProposalFromHash() },
  )

  watch(
    () => activeBoardFilter.value,
    () => { void loadProposals() },
  )

  return {
    proposals,
    proposalsLoading,
    availableBoards,
    loadingBoards,
    boardFilterInput,
    activeBoardFilter,
    activeBoardName,
    showCompleted,
    boardOptions,
    visibleProposals,
    summaryCards,
    dismissableProposalIds,
    matchesActiveBoardFilter,
    isProposalExpired,
    loadProposals,
    loadBoardOptions,
    startClock,
    stopClock,
    openInbox,
    proposalHref,
    captureHrefForProposal,
    openRoute,
    openBoard,
    applyBoardFilter,
    clearBoardFilter,
  }
}
