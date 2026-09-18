import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import PaperHomeView from '../../../views/paper/PaperHomeView.vue'
import type { HomeSummary } from '../../../types/workspace'

const mockSessionStore = reactive({ username: 'daniel' as string | null })
const mockWorkspaceStore = reactive({
  homeSummary: null as HomeSummary | null,
  homeLoading: false,
  homeError: null as string | null,
  hasHomeSummary: true,
  onboarding: null,
  fetchHomeSummary: vi.fn(async () => undefined),
  updateOnboarding: vi.fn(async () => undefined),
})
const mockCaptureStore = { createItem: vi.fn() }

vi.mock('../../../store/sessionStore', () => ({
  useSessionStore: () => mockSessionStore,
}))
vi.mock('../../../store/workspaceStore', () => ({
  useWorkspaceStore: () => mockWorkspaceStore,
}))
vi.mock('../../../store/captureStore', () => ({
  useCaptureStore: () => mockCaptureStore,
}))
vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn() }),
}))

function summary(): HomeSummary {
  return {
    workspaceMode: 'guided',
    isFirstRun: false,
    onboarding: {
      visibility: 'active',
      isComplete: false,
      currentStepId: null,
      dismissedAt: null,
      completedAt: null,
      steps: [],
    },
    workload: {
      capturesNeedingTriage: 0,
      capturesInProgress: 0,
      capturesReadyForFollowUp: 0,
      proposalsPendingReview: 0,
    },
    boards: { totalBoards: 1, recentBoardsCount: 0, recentBoards: [] },
    recommendedActions: [],
  }
}

describe('Paper Home capture shortcut boundary', () => {
  beforeEach(() => {
    mockWorkspaceStore.homeSummary = summary()
    mockWorkspaceStore.homeLoading = false
    mockWorkspaceStore.homeError = null
    mockWorkspaceStore.hasHomeSummary = true
  })

  afterEach(() => {
    document.querySelectorAll('[data-shortcut-fixture]').forEach((node) => node.remove())
  })

  it('does not steal Cmd/Ctrl+; from text entry and still handles it from page content', () => {
    const wrapper = mount(PaperHomeView, {
      attachTo: document.body,
      global: {
        stubs: {
          Teleport: true,
          WorkspaceSetupModal: true,
        },
      },
    })
    const capture = wrapper.get('[data-testid="paper-home-capture-input"]')
      .element as HTMLInputElement

    const textEntry = document.createElement('input')
    textEntry.dataset.shortcutFixture = 'text-entry'
    document.body.append(textEntry)
    textEntry.focus()
    textEntry.dispatchEvent(new KeyboardEvent('keydown', {
      key: ';',
      ctrlKey: true,
      bubbles: true,
      cancelable: true,
    }))

    expect(document.activeElement).toBe(textEntry)

    const pageButton = document.createElement('button')
    pageButton.dataset.shortcutFixture = 'page-content'
    document.body.append(pageButton)
    pageButton.focus()
    pageButton.dispatchEvent(new KeyboardEvent('keydown', {
      key: ';',
      ctrlKey: true,
      bubbles: true,
      cancelable: true,
    }))

    expect(document.activeElement).toBe(capture)
    wrapper.unmount()
  })
})
