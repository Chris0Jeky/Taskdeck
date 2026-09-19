import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { defineComponent, reactive, ref } from 'vue'
import PaperInboxView from '../../../views/paper/PaperInboxView.vue'
import type { CaptureItem, CaptureItemSummary } from '../../../types/capture'

const captureStore = reactive({
  loadingList: false,
  listError: null as string | null,
  actionBusyItemId: null as string | null,
  triagePollingItemIds: new Set<string>(),
  triagePollingProblems: {} as Record<string, 'retrying' | 'unavailable'>,
  triagePollingPaused: false,
  retryTriagePolling: vi.fn(),
  stopTriagePolling: vi.fn(),
  createItem: vi.fn(),
  triageItem: vi.fn(),
  keepItem: vi.fn(),
  archiveItem: vi.fn(),
  ignoreItem: vi.fn(),
  pollTriageCompletion: vi.fn(),
  peekDetail: vi.fn<(...args: unknown[]) => Promise<CaptureItem>>(),
})

const archivedRow: CaptureItemSummary = {
  id: 'archived-capture',
  userId: 'user-1',
  boardId: 'board-archived',
  status: 'ProposalCreated',
  source: 'Typed',
  textExcerpt: 'Retained capture',
  createdAt: '2026-09-18T00:00:00Z',
  processedAt: '2026-09-18T00:01:00Z',
}

const orchestrator = {
  captureStore,
  items: ref<CaptureItemSummary[]>([archivedRow]),
  activeBoardId: ref<string | null>('board-archived'),
  isArchivedHistory: ref(true),
  isScopeReplacement: ref(false),
  activeBoardName: ref('Archived board'),
  loadInbox: vi.fn<() => Promise<void>>(),
  clearScope: vi.fn<() => Promise<void>>(),
}

vi.mock('../../../composables/useInboxOrchestrator', () => ({
  useInboxOrchestrator: () => orchestrator,
}))

vi.mock('../../../store/sessionStore', () => ({
  useSessionStore: () => ({ userId: 'user-1' }),
}))

const PaperTriageTableStub = defineComponent({
  name: 'PaperTriageTable',
  emits: ['open'],
  template: '<button data-testid="open-archived" @click="$emit(\'open\', \'archived-capture\')">Open</button>',
})

describe('PaperInboxView archived detail freshness', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    orchestrator.items.value = [archivedRow]
    orchestrator.activeBoardId.value = 'board-archived'
    orchestrator.isArchivedHistory.value = true
    orchestrator.isScopeReplacement.value = false
    orchestrator.activeBoardName.value = 'Archived board'
    orchestrator.loadInbox.mockResolvedValue(undefined)
    orchestrator.clearScope.mockResolvedValue(undefined)
    captureStore.peekDetail.mockResolvedValue({
      ...archivedRow,
      rawText: 'Authoritative retained capture text',
      retryCount: 0,
      provenance: {
        captureItemId: archivedRow.id,
        triageRunId: 'run-current',
        proposalId: 'proposal-current',
        promptVersion: 'prompt-current',
      },
    } as CaptureItem)
  })

  it('bypasses cached detail when opening retained archived evidence', async () => {
    const wrapper = mount(PaperInboxView, {
      global: {
        stubs: {
          PaperCaptureNib: true,
          PaperCaptureComposer: true,
          PaperTriageTable: PaperTriageTableStub,
          InboxPollingNotice: true,
          PaperHLBtn: true,
          PaperScopeDisclosure: true,
          RouterLink: true,
        },
      },
    })

    await wrapper.get('[data-testid="open-archived"]').trigger('click')
    await flushPromises()

    expect(captureStore.peekDetail).toHaveBeenCalledWith('archived-capture', {
      forceRefresh: true,
      recordError: false,
      showToast: false,
    })

    wrapper.unmount()
  })
})
