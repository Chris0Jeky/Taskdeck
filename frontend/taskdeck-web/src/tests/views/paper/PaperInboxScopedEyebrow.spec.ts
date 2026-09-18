import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive, ref } from 'vue'
import PaperInboxView from '../../../views/paper/PaperInboxView.vue'
import { i18n } from '../../../i18n'
import type { CaptureItemSummary } from '../../../types/capture'

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
  peekDetail: vi.fn(),
})

const orchestrator = {
  captureStore,
  items: ref<CaptureItemSummary[]>([]),
  activeBoardId: ref<string | null>(null),
  isArchivedHistory: ref(false),
  isScopeReplacement: ref(false),
  activeBoardName: ref(''),
  loadInbox: vi.fn<() => Promise<void>>(),
  clearScope: vi.fn<() => Promise<void>>(),
}

vi.mock('../../../composables/useInboxOrchestrator', () => ({
  useInboxOrchestrator: () => orchestrator,
}))

vi.mock('../../../store/sessionStore', () => ({
  useSessionStore: () => ({ userId: 'user-1' }),
}))

function capture(id: string, status: CaptureItemSummary['status']): CaptureItemSummary {
  return {
    id,
    userId: 'user-1',
    boardId: orchestrator.activeBoardId.value,
    status,
    source: 'Typed',
    textExcerpt: id,
    createdAt: '2026-09-18T00:00:00Z',
    processedAt: null,
  }
}

function mountView() {
  return mount(PaperInboxView, {
    global: {
      stubs: {
        PaperCaptureNib: true,
        PaperCaptureComposer: true,
        PaperTriageTable: true,
        InboxPollingNotice: true,
        PaperHLBtn: true,
        PaperScopeDisclosure: true,
      },
    },
  })
}

describe('PaperInboxView scoped eyebrow', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    i18n.global.locale.value = 'en'
    orchestrator.items.value = []
    orchestrator.activeBoardId.value = null
    orchestrator.activeBoardName.value = ''
    orchestrator.isArchivedHistory.value = false
    orchestrator.isScopeReplacement.value = false
    orchestrator.loadInbox.mockResolvedValue(undefined)
    orchestrator.clearScope.mockResolvedValue(undefined)
  })

  afterEach(() => {
    i18n.global.locale.value = 'en'
  })

  it('names the board scope when its counts differ from the workspace badge', () => {
    orchestrator.activeBoardId.value = 'board-active'
    orchestrator.activeBoardName.value = 'Active board'
    orchestrator.items.value = [capture('pending', 'New'), capture('finished', 'Converted')]

    const wrapper = mountView()
    const eyebrow = wrapper.get('[data-testid="paper-inbox-eyebrow"]').text()

    expect(eyebrow).toBe(
      'Inbox · Active board · 1 awaiting triage · 2 captured on this board',
    )
    wrapper.unmount()
  })

  it('keeps the existing workspace wording when no board filter is active', () => {
    orchestrator.items.value = [capture('pending', 'New'), capture('finished', 'Converted')]

    const wrapper = mountView()
    const eyebrow = wrapper.get('[data-testid="paper-inbox-eyebrow"]').text()

    expect(eyebrow).toBe('Inbox · capture surface · 1 awaiting triage · 2 captured')
    expect(eyebrow).not.toContain('on this board')
    wrapper.unmount()
  })
})
