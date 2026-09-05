import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { captureApi, type CaptureReadOptions } from '../api/captureApi'
import { isTriageTerminalStatus } from '../types/capture'
import type { BatchTriageAction, BatchTriageResult, CaptureItem, CaptureItemSummary, CaptureListQuery, CreateCaptureItemDto, UpdateCaptureSuggestionDto } from '../types/capture'
import { useToastStore } from './toastStore'
import { useWorkspaceStore } from './workspaceStore'
import { getErrorDisplay, getErrorDetails } from '../composables/useErrorMapper'
import { isDemoMode, DemoModeError } from '../utils/demoMode'
import { buildDemoCaptureItems } from '../utils/demoData'

function toSummary(item: CaptureItem): CaptureItemSummary {
  return {
    id: item.id,
    userId: item.userId,
    boardId: item.boardId,
    status: item.status,
    source: item.source,
    textExcerpt: item.textExcerpt,
    createdAt: item.createdAt,
    processedAt: item.processedAt,
    errorMessage: item.errorMessage ?? null,
    disposition: item.disposition ?? null,
  }
}

type DetailLoadOptions = {
  forceRefresh?: boolean
  recordError?: boolean
  showToast?: boolean
  syncSummary?: boolean
  requestOptions?: CaptureReadOptions
  shouldCache?: () => boolean
  /**
   * Whether this read owns the store-wide `loadingDetail` flag (default true).
   *
   * That flag is what the open detail panel renders from: while it is set the
   * panel body is replaced by a "Refreshing detail..." spinner and its Refresh
   * Detail button is disabled. A background reconciliation of some OTHER
   * capture must not do that to the detail the user is reading (#2304), so the
   * quiet reads pass `false` and leave the flag to foreground loads.
   */
  trackLoading?: boolean
}

export const BATCH_TRIAGE_POLL_INTERVAL_MS = 3_000
export const BATCH_TRIAGE_POLL_MAX_DURATION_MS = 60_000
const BATCH_TRIAGE_POLL_TIMEOUT_MESSAGE =
  'Automatic checking stopped after 60 seconds. Triage may still be running. Use Refresh Detail to check the result.'

type CreateItemOptions = {
  /**
   * Whether the badge refresh fires after the capture lands (default true).
   *
   * Pass `false` from a caller that runs its OWN `fetchHomeSummary()` next:
   * both read GET /workspace/home — the heaviest endpoint on the surface —
   * and the full summary already rewrites `workload`, so leaving the notify
   * on would fetch it twice for one keystroke. The full fetch is what those
   * callers need anyway: a capture also moves `onboarding` (the
   * `capture-first-item` milestone is `TotalCaptures > 0` server-side) and
   * `recommendedActions`, neither of which the workload-only refresh carries.
   */
  refreshWorkload?: boolean
}

export const useCaptureStore = defineStore('capture', () => {
  const toast = useToastStore()
  const workspace = useWorkspaceStore()

  /**
   * Tell the sidebar badges a capture's triage state moved (#1974).
   *
   * The badges read a server-computed workload count (`New + Failed`), which
   * is fetched once per session; without this the badge kept a pre-mutation
   * number until a full page reload. Fire-and-forget on purpose: the mutation
   * has already succeeded and its own toast has already been shown, so a badge
   * that refreshes a beat later must never delay or fail the action.
   *
   * Called only after a mutation that can change `New + Failed`:
   * create (+1), triage (-1), ignore/cancel (-1), batch, and a triage poll
   * reaching a terminal status (`Failed` puts one back).
   */
  function notifyTriageCountChanged() {
    void workspace.refreshWorkloadCounts()
  }

  function guardDemoMutation(): never | void {
    if (isDemoMode) {
      toast.info('This action is view-only in demo mode.')
      throw new DemoModeError()
    }
  }

  /**
   * Raise the standard persistent error toast, attaching an inspectable request
   * diagnostic (status, endpoint, correlation id) when the failure carries one
   * (GH-1938) so the expander and Copy on the toast receipt become functional.
   * When no diagnostic is available the call shape is unchanged
   * (`toast.error(message)`), leaving unrelated error toasts identical.
   */
  function reportCaptureError(message: string, error: unknown) {
    const details = getErrorDetails(error)
    if (details) {
      toast.error(message, undefined, { details })
    } else {
      toast.error(message)
    }
  }

  const items = ref<CaptureItemSummary[]>([])
  const detailById = ref<Record<string, CaptureItem>>({})
  const loadingList = ref(false)
  const loadingDetail = ref(false)
  let latestListLoadRequestId = 0
  // One monotonic clock for both guards below. A write records it to reject
  // older reads; a summary records it so an older BACKGROUND list snapshot
  // cannot regress a row that moved after that read began (#2301).
  let nextCaptureGeneration = 0
  let latestListWriteGeneration = 0
  const latestDetailWriteGenerationById = new Map<string, number>()
  const latestSummaryGenerationById = new Map<string, number>()
  const actionBusyItemId = ref<string | null>(null)
  const listError = ref<string | null>(null)
  const detailError = ref<string | null>(null)
  const actionError = ref<string | null>(null)

  const hasItems = computed(() => items.value.length > 0)

  function upsertSummary(summary: CaptureItemSummary) {
    // Every per-item summary write — an explicit detail load, the single-item
    // triage poll, an optimistic mutation — stamps the shared clock so a list
    // read that started earlier cannot overwrite it with older status.
    latestSummaryGenerationById.set(summary.id, ++nextCaptureGeneration)
    const existingIndex = items.value.findIndex((item) => item.id === summary.id)
    if (existingIndex >= 0) {
      items.value[existingIndex] = summary
      return
    }

    items.value.unshift(summary)
  }

  function cacheDetail(detail: CaptureItem, syncSummary = true) {
    detailById.value[detail.id] = detail
    if (syncSummary) {
      upsertSummary(toSummary(detail))
    }
  }

  /**
   * Mark a successful server write before its response enters either cache.
   * Reads that started before this generation may still return to their
   * callers, but they must not replace the newer mutation response.
   */
  function recordCaptureWrite(itemId: string, syncSummary: boolean) {
    const generation = ++nextCaptureGeneration
    latestDetailWriteGenerationById.set(itemId, generation)
    if (syncSummary) {
      latestListWriteGeneration = generation
    }
  }

  function detailWriteGeneration(itemId: string): number {
    return latestDetailWriteGenerationById.get(itemId) ?? 0
  }

  /**
   * Apply a BACKGROUND list snapshot without regressing rows that moved after
   * the read began (#2301).
   *
   * `latestListWriteGeneration` rejects a whole snapshot that a mutation has
   * outdated, but the single-item triage poll and an explicit detail load are
   * READS: they write a fresher summary through `upsertSummary` without
   * recording a capture write, so a slower batch-list response landing after
   * them used to put the row back to `Triaging`. The snapshot stays the
   * authority for membership and order — the list still owns scope and the
   * newest-first cap — and only a row whose own summary is newer than this
   * read keeps its local value.
   */
  function applyBackgroundListSnapshot(
    loadedItems: CaptureItemSummary[],
    observedGeneration: number,
  ) {
    const currentById = new Map(items.value.map((item) => [item.id, item]))
    items.value = loadedItems.map((loaded) => {
      if ((latestSummaryGenerationById.get(loaded.id) ?? 0) <= observedGeneration) return loaded
      return currentById.get(loaded.id) ?? loaded
    })
  }

  /**
   * Load the Inbox list, and REPORT whether this call's response was applied
   * (#2501).
   *
   * A superseded call resolves without writing anything — twice over, once in
   * the success path and once in the catch — so resolution alone never meant
   * "the rows on screen are now this call's rows". A caller that inferred
   * success from resolution therefore acted on a response the store had
   * dropped; `useInboxOrchestrator` cleared its scope-replacement flag that
   * way, un-hiding the retained OLD-scope rows under the NEW scope's label.
   *
   * `true` means this response was written into `items`. `false` means it was
   * dropped as superseded and the caller's assumptions about `items` are
   * unchanged. A failure that is still the latest request throws, as before, so
   * failure and supersession stay distinguishable. The value is additive:
   * existing `await fetchItems(...)` call sites that ignore it are unaffected.
   */
  async function fetchItems(query?: CaptureListQuery): Promise<boolean> {
    const requestId = ++latestListLoadRequestId
    if (isDemoMode) {
      loadingList.value = true
      listError.value = null
      // The guard is pre-existing and cannot currently fail: nothing awaits
      // between the id bump above and this check, so no other call can have
      // superseded this one. It is kept, with its `false` arm, so the branch
      // stays correct and total if the demo path ever becomes genuinely async.
      if (requestId === latestListLoadRequestId) {
        items.value = buildDemoCaptureItems()
        loadingList.value = false
        return true
      }
      return false
    }

    try {
      loadingList.value = true
      listError.value = null
      const loadedItems = await captureApi.listItems(query)
      if (requestId !== latestListLoadRequestId) return false
      // This is the explicit/user-facing list load. A successful mutation may
      // finish while a scope replacement is in flight, but that must not make
      // the newer scope response disappear. The request id still gives the
      // usual latest-load-wins ordering; background batch polls keep the write
      // generation guard in their own reader below.
      items.value = loadedItems
      return true
    } catch (e: unknown) {
      if (requestId !== latestListLoadRequestId) return false
      const message = getErrorDisplay(e, 'Failed to load inbox items').message
      listError.value = message
      toast.error(message)
      throw e
    } finally {
      if (requestId === latestListLoadRequestId) {
        loadingList.value = false
      }
    }
  }

  async function fetchDetail(itemId: string, options: DetailLoadOptions = {}) {
    const {
      forceRefresh = false,
      recordError = true,
      showToast = true,
      syncSummary = true,
      requestOptions,
      shouldCache = () => true,
      trackLoading = true,
    } = options

    if (!forceRefresh && detailById.value[itemId]) {
      return detailById.value[itemId]
    }

    if (isDemoMode) {
      const summary = items.value.find((i) => i.id === itemId)
      if (summary) {
        const detail = { ...summary, rawText: summary.textExcerpt, retryCount: 0, provenance: null }
        cacheDetail(detail, syncSummary)
        return detail
      }
    }

    const observedDetailWriteGeneration = detailWriteGeneration(itemId)
    try {
      if (trackLoading) {
        loadingDetail.value = true
      }
      if (recordError) {
        detailError.value = null
      }
      const detail = requestOptions
        ? await captureApi.getItem(itemId, requestOptions)
        : await captureApi.getItem(itemId)
      if (!shouldCache()) return detail
      if (observedDetailWriteGeneration !== detailWriteGeneration(itemId)) return detail
      cacheDetail(detail, syncSummary)
      return detail
    } catch (e: unknown) {
      const message = getErrorDisplay(e, 'Failed to load inbox item').message
      if (recordError) {
        detailError.value = message
      }
      if (showToast) {
        toast.error(message)
      }
      throw e
    } finally {
      if (trackLoading) {
        loadingDetail.value = false
      }
    }
  }

  async function peekDetail(itemId: string, options: DetailLoadOptions = {}) {
    const {
      forceRefresh = false,
      recordError = true,
      showToast = true,
    } = options

    if (!forceRefresh && detailById.value[itemId]) {
      return detailById.value[itemId]
    }

    try {
      loadingDetail.value = true
      if (recordError) {
        detailError.value = null
      }
      return await captureApi.getItem(itemId)
    } catch (e: unknown) {
      const message = getErrorDisplay(e, 'Failed to load inbox item').message
      if (recordError) {
        detailError.value = message
      }
      if (showToast) {
        toast.error(message)
      }
      throw e
    } finally {
      loadingDetail.value = false
    }
  }

  async function createItem(dto: CreateCaptureItemDto, options: CreateItemOptions = {}) {
    guardDemoMutation()
    try {
      actionError.value = null
      const created = await captureApi.createItem(dto)
      recordCaptureWrite(created.id, true)
      detailById.value[created.id] = created
      upsertSummary(toSummary(created))
      // SAVED, not APPLIED (#1970): a capture sitting in the inbox has touched
      // no board. Duration stays the store default; only the stamp is named.
      toast.success('Capture saved to inbox', undefined, { label: 'saved' })
      if (options.refreshWorkload !== false) {
        notifyTriageCountChanged()
      }
      return created
    } catch (e: unknown) {
      const message = getErrorDisplay(e, 'Failed to capture item').message
      actionError.value = message
      reportCaptureError(message, e)
      throw e
    }
  }

  async function ignoreItem(itemId: string) {
    guardDemoMutation()
    try {
      actionBusyItemId.value = itemId
      actionError.value = null
      await captureApi.ignoreItem(itemId)
      recordCaptureWrite(itemId, true)
      await fetchDetail(itemId, { forceRefresh: true })
      toast.success('Capture item ignored')
      notifyTriageCountChanged()
    } catch (e: unknown) {
      const message = getErrorDisplay(e, 'Failed to ignore capture item').message
      actionError.value = message
      toast.error(message)
      throw e
    } finally {
      actionBusyItemId.value = null
    }
  }

  async function keepItem(itemId: string) {
    guardDemoMutation()
    try {
      actionBusyItemId.value = itemId
      actionError.value = null
      const kept = await captureApi.keepItem(itemId)
      const syncSummary = items.value.some((item) => item.id === itemId)
      recordCaptureWrite(itemId, syncSummary)
      cacheDetail(kept, syncSummary)
      toast.success('Capture kept for later', undefined, { label: 'saved' })
      notifyTriageCountChanged()
      return kept
    } catch (e: unknown) {
      const message = getErrorDisplay(e, 'Failed to keep capture item').message
      actionError.value = message
      toast.error(message)
      throw e
    } finally {
      actionBusyItemId.value = null
    }
  }

  async function archiveItem(itemId: string) {
    guardDemoMutation()
    try {
      actionBusyItemId.value = itemId
      actionError.value = null
      const archived = await captureApi.archiveItem(itemId)
      const syncSummary = items.value.some((item) => item.id === itemId)
      recordCaptureWrite(itemId, syncSummary)
      cacheDetail(archived, syncSummary)
      toast.success('Capture archived', undefined, { label: 'saved' })
      notifyTriageCountChanged()
      return archived
    } catch (e: unknown) {
      const message = getErrorDisplay(e, 'Failed to archive capture item').message
      actionError.value = message
      toast.error(message)
      throw e
    } finally {
      actionBusyItemId.value = null
    }
  }

  async function cancelItem(itemId: string) {
    guardDemoMutation()
    try {
      actionBusyItemId.value = itemId
      actionError.value = null
      await captureApi.cancelItem(itemId)
      recordCaptureWrite(itemId, true)
      await fetchDetail(itemId, { forceRefresh: true })
      toast.success('Capture item cancelled')
      notifyTriageCountChanged()
    } catch (e: unknown) {
      const message = getErrorDisplay(e, 'Failed to cancel capture item').message
      actionError.value = message
      toast.error(message)
      throw e
    } finally {
      actionBusyItemId.value = null
    }
  }

  const triagePollingItemId = ref<string | null>(null)
  let activeTriagePollStop: (() => void) | null = null

  function pollTriageCompletion(itemId: string): () => void {
    const POLL_INTERVAL_MS = 2_000
    // About 15 minutes at the normal cadence; #1585 owns provider-aware elapsed-time policy.
    const MAX_POLLS = 450
    let pollCount = 0
    let stopped = false
    let timerId: ReturnType<typeof setTimeout> | null = null

    if (activeTriagePollStop) {
      activeTriagePollStop()
    }

    triagePollingItemId.value = itemId

    async function tick() {
      if (stopped) return
      pollCount++

      try {
        const observedDetailWriteGeneration = detailWriteGeneration(itemId)
        const detail = await captureApi.getItem(itemId)
        if (stopped) return
        if (observedDetailWriteGeneration === detailWriteGeneration(itemId)) {
          cacheDetail(detail)

          if (isTriageTerminalStatus(detail.status)) {
            // Triage finished while the user watched: the badge moves again here
            // (a `Failed` outcome puts the capture back into the pending count).
            notifyTriageCountChanged()
            stop()
            return
          }
        }
      } catch {
        // Silently retry on transient errors; the manual refresh button is still available.
      }

      if (!stopped && pollCount < MAX_POLLS) {
        timerId = setTimeout(tick, POLL_INTERVAL_MS)
      } else {
        stop()
      }
    }

    function stop() {
      stopped = true
      if (timerId !== null) {
        clearTimeout(timerId)
        timerId = null
      }
      if (activeTriagePollStop === stop) {
        activeTriagePollStop = null
      }
      if (triagePollingItemId.value === itemId) {
        triagePollingItemId.value = null
      }
    }

    activeTriagePollStop = stop
    timerId = setTimeout(tick, POLL_INTERVAL_MS)
    return stop
  }

  async function triageItem(itemId: string, boardId?: string | null) {
    guardDemoMutation()
    try {
      actionBusyItemId.value = itemId
      actionError.value = null
      const triageResult = await captureApi.enqueueTriage(itemId, boardId)

      const existingDetail = detailById.value[itemId]
      const existingSummary = items.value.find((item) => item.id === itemId)
      // An uncached item has no summary to protect. Avoid invalidating an
      // explicit list load solely because the detail generation advanced.
      const syncSummary = Boolean(existingDetail || existingSummary)
      recordCaptureWrite(itemId, syncSummary)
      let optimisticDetail: CaptureItem | null = null
      if (existingDetail) {
        optimisticDetail = {
          ...existingDetail,
          status: triageResult.status,
          disposition: null,
        }
        detailById.value[itemId] = optimisticDetail
      }

      if (existingSummary) {
        upsertSummary({
          ...existingSummary,
          status: triageResult.status,
          disposition: null,
        })
      } else if (optimisticDetail) {
        upsertSummary(toSummary(optimisticDetail))
      }

      // The enqueue is the write. A transient follow-up GET failure must not report that
      // successful write as failed or prevent the caller from polling the queued work.
      await fetchDetail(itemId, { forceRefresh: true, showToast: false }).catch(() => undefined)
      // QUEUED (#1970): triage has been enqueued, not run and not applied.
      // Both branches are the same outcome class — the queue already holds it.
      toast.success(
        triageResult.alreadyTriaging ? 'Capture item is already triaging' : 'Capture item triage queued',
        undefined,
        { label: 'queued' },
      )
      notifyTriageCountChanged()
      return triageResult
    } catch (e: unknown) {
      const message = getErrorDisplay(e, 'Failed to triage capture item').message
      actionError.value = message
      reportCaptureError(message, e)
      throw e
    } finally {
      actionBusyItemId.value = null
    }
  }

  /**
   * Reconcile cached DETAILS against the list snapshot just fetched (#2202,
   * PR #2224 review).
   *
   * `batchTriage` enqueues and then re-reads the LIST. The per-item detail
   * cache is untouched by that read, so a capture whose triage had already
   * finished still rendered from its pre-batch `New`/`Triaging` detail — and
   * with it, no `errorMessage`, so the degradation notice was absent on the
   * open Legacy panel until the user refreshed by hand.
   *
   * Both the immediate post-write snapshot and the bounded batch completion
   * poll use this reconciliation. A failing follow-up GET is
   * swallowed for the same reason the single-item path swallows its own — the
   * batch write already succeeded and must not be reported as failed.
   */
  async function refreshTerminalDetails(
    itemIds: string[],
    options: {
      requestOptions?: CaptureReadOptions
      isCurrent?: () => boolean
      onRefreshed?: (itemId: string) => void
    } = {},
  ): Promise<void> {
    const isCurrent = options.isCurrent ?? (() => true)
    const stale = itemIds.filter((id) => {
      const detail = detailById.value[id]
      const summary = items.value.find((item) => item.id === id)
      // A tracked item can fall beyond the newest-first list cap. Its detail
      // is then the only authoritative surface, so fetch it directly even when
      // the user selected the row without previously opening/caching it.
      if (!summary) return true
      if (!detail) return false
      if (!isTriageTerminalStatus(summary.status)) return false
      return (
        summary.status !== detail.status ||
        (summary.errorMessage ?? null) !== (detail.errorMessage ?? null)
      )
    })

    await Promise.all(
      stale.map(async (id) => {
        try {
          await fetchDetail(id, {
            forceRefresh: true,
            showToast: false,
            recordError: false,
            // The list snapshot remains the summary authority. If the detail
            // endpoint lags it briefly, do not regress the row back to Triaging
            // or reinsert an item that fell beyond the visible list cap.
            syncSummary: false,
            // Quiet in every respect, for BOTH callers: this reconciliation
            // runs over the tracked batch, not over whatever detail is open,
            // so it must not take the panel-wide loading flag away from that
            // detail (#2304).
            //
            // `batchTriage` is a FOREGROUND caller and still reconciles
            // quietly on purpose (#2571). `loadingDetail` is one store-wide
            // boolean: raising it here would blank the panel and disable
            // Refresh Detail for an open capture that is not in the batch
            // selection, and the first of these parallel reads to settle would
            // clear the flag under any genuine foreground detail load still in
            // flight. The foreground feedback for a batch is `batchBusy`,
            // which stays true for the whole `batchTriage` body and renders
            // "Processing" in the list panel.
            trackLoading: false,
            requestOptions: options.requestOptions,
            shouldCache: isCurrent,
          })
          if (isCurrent()) options.onRefreshed?.(id)
        } catch {
          // A later poll tick retries transient detail failures.
        }
      }),
    )
  }

  function pollBatchTriageCompletion(
    itemIds: string[],
    query?: CaptureListQuery,
  ): () => void {
    const trackedIds = [...new Set(itemIds)]
    let stopped = false
    let timerId: ReturnType<typeof setTimeout> | null = null
    let deadlineTimerId: ReturnType<typeof setTimeout> | null = null
    let activeRequest: AbortController | null = null
    const refreshedDetailIds = new Set<string>()
    const countedTerminalIds = new Set<string>()
    let observedPostEnqueueList = false

    /**
     * A tracked item whose terminal outcome this poll has actually observed
     * since the batch was enqueued — the same truth `isComplete` reads, one id
     * at a time. Cached pre-batch state never qualifies.
     */
    function isObservedTerminal(itemId: string): boolean {
      const summary = items.value.find((item) => item.id === itemId)
      if (summary) {
        return observedPostEnqueueList && isTriageTerminalStatus(summary.status)
      }
      const detail = detailById.value[itemId]
      return Boolean(
        detail &&
        refreshedDetailIds.has(itemId) &&
        isTriageTerminalStatus(detail.status),
      )
    }

    /**
     * Move the badges for outcomes this poll observed but has not counted yet
     * (#2303).
     *
     * The count is `New + Failed`, so a single item finishing changes it —
     * waiting for the whole batch left the sidebar and Home stale for up to a
     * minute whenever one item lagged, and stale forever when the deadline
     * stopped the poll first. The counted set makes this idempotent: an
     * unchanged snapshot notifies nobody.
     */
    function refreshCountsForNewTerminalOutcomes() {
      let observedNewOutcome = false
      for (const id of trackedIds) {
        if (countedTerminalIds.has(id)) continue
        if (!isObservedTerminal(id)) continue
        countedTerminalIds.add(id)
        observedNewOutcome = true
      }
      if (observedNewOutcome) {
        notifyTriageCountChanged()
      }
    }

    function stop() {
      if (stopped) return
      stopped = true
      if (timerId !== null) {
        clearTimeout(timerId)
        timerId = null
      }
      if (deadlineTimerId !== null) {
        clearTimeout(deadlineTimerId)
        deadlineTimerId = null
      }
      if (activeRequest) {
        activeRequest.abort()
        activeRequest = null
      }
    }

    function stopAtDeadline() {
      if (stopped) return
      // An outcome this poll already observed still moved the workload count,
      // even when the tick that saw it was aborted here before reconciling.
      refreshCountsForNewTerminalOutcomes()
      // The batch write already succeeded. A deadline only means automatic
      // checking stopped; the server-side triage may still be running.
      if (!isComplete()) {
        batchError.value = BATCH_TRIAGE_POLL_TIMEOUT_MESSAGE
        toast.warning(BATCH_TRIAGE_POLL_TIMEOUT_MESSAGE, 0)
      }
      stop()
    }

    function isComplete(): boolean {
      return trackedIds.every((id) => {
        const summary = items.value.find((item) => item.id === id)
        const detail = detailById.value[id]
        if (!summary) {
          return Boolean(
            detail &&
            refreshedDetailIds.has(id) &&
            isTriageTerminalStatus(detail.status),
          )
        }
        if (!isTriageTerminalStatus(summary.status)) return false
        if (!observedPostEnqueueList) return false
        if (!detail) return true
        return (
          detail.status === summary.status &&
          (detail.errorMessage ?? null) === (summary.errorMessage ?? null)
        )
      })
    }

    function scheduleNext() {
      if (stopped) return
      timerId = setTimeout(() => {
        timerId = null
        void tick()
      }, BATCH_TRIAGE_POLL_INTERVAL_MS)
    }

    async function tick() {
      if (stopped) return
      // An explicit list load owns the visible loading state and is fresher
      // user intent. Let it finish before the quiet background poll tries.
      if (loadingList.value) {
        scheduleNext()
        return
      }

      const observedListLoadRequestId = latestListLoadRequestId
      const observedListWriteGeneration = latestListWriteGeneration
      const observedSummaryGeneration = nextCaptureGeneration
      const controller = new AbortController()
      activeRequest = controller
      const requestOptions = { signal: controller.signal, skipRetry: true }
      const isCurrent = () =>
        !stopped &&
        !controller.signal.aborted &&
        activeRequest === controller &&
        observedListLoadRequestId === latestListLoadRequestId &&
        observedListWriteGeneration === latestListWriteGeneration

      try {
        const loadedItems = await captureApi.listItems(query, requestOptions)
        if (!isCurrent()) return
        applyBackgroundListSnapshot(loadedItems, observedSummaryGeneration)
        // A foreground read failure hides every row behind its message, so a
        // batch whose immediate post-POST refresh exhausted its retries left
        // the inbox looking empty-and-broken until the user pressed Retry
        // (#2305). This accepted snapshot is proof the same list is readable
        // again, and it is the rows now on screen. Only this success path
        // clears the error: an aborted, superseded (newer explicit load or
        // newer capture write), 401 or 403 response fails `isCurrent()` or
        // lands in the catch below and leaves the foreground error standing.
        listError.value = null
        observedPostEnqueueList = true
        await refreshTerminalDetails(trackedIds, {
          requestOptions,
          isCurrent,
          onRefreshed: (id) => refreshedDetailIds.add(id),
        })
        if (!isCurrent()) return
        refreshCountsForNewTerminalOutcomes()
        if (isComplete()) {
          stop()
          return
        }
      } catch (error: unknown) {
        if (stopped || controller.signal.aborted) return
        const status = (error as { response?: { status?: number } } | null)?.response?.status
        // 401 keeps the shared auth-expiry redirect; 403 means this scope is no
        // longer readable. Neither should be hammered by a background timer.
        if (status === 401 || status === 403) {
          stop()
          return
        }
        // Other background-read failures stay silent and retry on the next tick.
      } finally {
        if (activeRequest === controller) activeRequest = null
        scheduleNext()
      }
    }

    if (trackedIds.length === 0 || isComplete()) {
      stop()
      return stop
    }
    // A separate deadline timer aborts an in-flight request at the boundary;
    // counting ticks alone would let one slow HTTP request exceed 60 seconds.
    deadlineTimerId = setTimeout(stopAtDeadline, BATCH_TRIAGE_POLL_MAX_DURATION_MS)
    scheduleNext()
    return stop
  }

  const batchBusy = ref(false)
  const batchError = ref<string | null>(null)

  /**
   * Enqueue a batch action, then reconcile the list.
   *
   * `query` is the CALLER'S list scope (#2570). The reconciliation read
   * replaces `items`, so an unscoped read under a board-scoped Inbox replaced
   * the visible rows with the unscoped list — for `ignore` and `cancel` no poll
   * follows, so those wrong rows persisted until the next scoped load. Callers
   * that pass nothing keep the unscoped read they always had.
   *
   * Pass a THUNK when the scope can move while the POST is in flight. It is
   * called once, immediately before the read is issued, so the read uses the
   * scope that is current THEN rather than the one that was current when the
   * batch started. That matters because `fetchItems` supersedes by request id:
   * a stale-scope read issued last wins the list and drops the caller's own
   * newer-scope load, writing the old board's rows under the new board's label.
   */
  async function batchTriage(
    itemIds: string[],
    action: BatchTriageAction,
    query?: CaptureListQuery | (() => CaptureListQuery),
  ): Promise<BatchTriageResult> {
    guardDemoMutation()
    batchBusy.value = true
    batchError.value = null
    actionError.value = null
    try {
      const batchItems = itemIds.map((id) => ({ itemId: id, action }))
      let result: BatchTriageResult
      try {
        result = await captureApi.batchTriage(batchItems)
      } catch (e: unknown) {
        const message = getErrorDisplay(e, 'Failed to process batch triage').message
        batchError.value = message
        toast.error(message)
        throw e
      }

      // Record every successful batch write before any reconciliation read. An
      // older detail poll must not restore the pre-batch status/disposition.
      for (const item of result.results) {
        if (item.success) recordCaptureWrite(item.itemId, true)
      }

      if (result.succeeded > 0) {
        toast.success(`${result.succeeded} of ${result.total} items processed`)
      }
      if (result.failed > 0) {
        const failedMessages = result.results
          .filter((r) => !r.success)
          .map((r) => r.errorMessage ?? 'Unknown error')
          .slice(0, 3)
          .join('; ')
        toast.error(`${result.failed} item(s) failed: ${failedMessages}`)
      }

      // The POST result is authoritative. Reconciliation still uses the
      // ordinary read path so retry, list-error reporting, and 401/session
      // interception remain intact, but an exhausted follow-up read must not
      // reclassify a successfully queued batch as a failed write or prevent
      // the caller from starting its bounded completion poll.
      try {
        // Resolve the scope HERE, at the moment the read is issued, so a scope
        // change during the POST is honoured rather than overwritten.
        //
        // The applied boolean is deliberately ignored: a post-batch read that a
        // newer list load superseded is not a failed write.
        await fetchItems(typeof query === 'function' ? query() : query)
        await refreshTerminalDetails(itemIds)
      } catch (e: unknown) {
        const status = (e as { response?: { status?: number } } | null)?.response?.status
        if (status === 401 || status === 403) throw e
        // fetchItems already records and surfaces the read failure. The poll
        // can retry the same list/detail reconciliation on its next tick.
      }
      notifyTriageCountChanged()

      return result
    } finally {
      batchBusy.value = false
    }
  }

  async function updateSuggestion(itemId: string, dto: UpdateCaptureSuggestionDto): Promise<CaptureItem> {
    guardDemoMutation()
    try {
      actionBusyItemId.value = itemId
      actionError.value = null
      const updated = await captureApi.updateSuggestion(itemId, dto)
      recordCaptureWrite(itemId, true)
      cacheDetail(updated)
      // SAVED, not APPLIED (GH-1970): correcting capture text or metadata
      // rewrites the capture and nothing else — no triage ran, no board was
      // touched. The stamp has to say so now that Paper renders it (GH-1951,
      // GH-2005).
      toast.success('Capture updated', undefined, { label: 'saved' })
      return updated
    } catch (e: unknown) {
      const message = getErrorDisplay(e, 'Failed to update capture').message
      actionError.value = message
      toast.error(message)
      throw e
    } finally {
      actionBusyItemId.value = null
    }
  }

  /**
   * Drop the per-item generation guards when the session ends (#2571).
   *
   * Both maps are keyed by capture id and take an entry for every summary
   * write and every successful mutation, so they otherwise grow for the
   * lifetime of the store with no eviction on scope change, list replacement
   * or logout. They are guards, not data: an empty map reads as generation 0,
   * which is the same "nothing recorded yet" a fresh store starts from.
   *
   * The shared clock itself stays monotonic on purpose. A detail read still in
   * flight from the previous session then compares against a generation it can
   * no longer match, so its response is dropped instead of cached.
   */
  function resetForLogout() {
    latestDetailWriteGenerationById.clear()
    latestSummaryGenerationById.clear()
  }

  return {
    items,
    detailById,
    loadingList,
    loadingDetail,
    actionBusyItemId,
    listError,
    detailError,
    actionError,
    hasItems,
    batchBusy,
    batchError,
    cacheDetail,
    fetchItems,
    fetchDetail,
    peekDetail,
    createItem,
    keepItem,
    archiveItem,
    ignoreItem,
    cancelItem,
    triageItem,
    triagePollingItemId,
    pollTriageCompletion,
    pollBatchTriageCompletion,
    batchTriage,
    updateSuggestion,
    resetForLogout,
  }
})
