import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { ref } from 'vue'
import DevToolsView from '../../views/DevToolsView.vue'

// Build the recorder mock after module imports resolve (don't use vi.hoisted for refs)
const recorderIsRecording = ref(false)
const recorderActionCount = ref(0)

const recorderStart = vi.fn<(name: string) => void>()
const recorderStop = vi.fn<
  () => {
    id: string
    name: string
    actions: unknown[]
    durationMs: number
    startedAt: string
    stoppedAt: string
  } | null
>()

vi.mock('../../composables/useTraceRecorder', () => ({
  useTraceRecorder: () => ({
    get isRecording() { return recorderIsRecording },
    get actionCount() { return recorderActionCount },
    start: recorderStart,
    stop: recorderStop,
  }),
}))

vi.mock('../../utils/traceReplay', () => ({
  createReplayEngine: vi.fn(() => ({
    onStateChange: vi.fn(),
    onAction: vi.fn(),
    play: vi.fn(),
    pause: vi.fn(),
    dispose: vi.fn(),
  })),
}))

vi.mock('../../utils/scenarioSchema', () => ({
  validateScenario: vi.fn(() => []),
  createBlankScenario: vi.fn(() => ({
    id: 'scenario-blank',
    name: 'New Scenario',
    description: '',
    steps: [],
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
  })),
  createBlankStep: vi.fn((type: string) => ({
    id: `step-${Date.now()}`,
    type,
    label: '',
    params: {},
  })),
  parseScenarioJson: vi.fn(() => ({
    scenario: null,
    errors: [{ path: '', message: 'Invalid JSON' }],
  })),
}))

async function waitForUi() {
  await Promise.resolve()
  await Promise.resolve()
}

describe('DevToolsView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    recorderIsRecording.value = false
    recorderActionCount.value = 0
    recorderStop.mockReturnValue(null)
  })

  it('renders the Dev Tools heading', () => {
    const wrapper = mount(DevToolsView)
    expect(wrapper.text()).toContain('Dev Tools')
  })

  it('renders Trace Recorder & Replay and Scenario Editor tabs', () => {
    const wrapper = mount(DevToolsView)
    expect(wrapper.text()).toContain('Trace Recorder & Replay')
    expect(wrapper.text()).toContain('Scenario Editor')
  })

  it('shows the trace tab content by default', () => {
    const wrapper = mount(DevToolsView)
    expect(wrapper.text()).toContain('Trace Recorder')
    expect(wrapper.text()).toContain('Start Recording')
  })

  it('switches to the Scenario Editor tab when clicked', async () => {
    const wrapper = mount(DevToolsView)

    const scenarioTab = wrapper.findAll('button').find((b) =>
      b.text().includes('Scenario Editor'),
    )
    expect(scenarioTab).toBeDefined()
    await scenarioTab!.trigger('click')
    await waitForUi()

    // Scenario editor heading appears in the panel
    expect(wrapper.text()).toContain('Scenario Editor')
    // The name input renders with the blank scenario's default name as its value
    const nameInput = wrapper.find('input[placeholder="Scenario name"]')
    expect(nameInput.exists()).toBe(true)
    expect((nameInput.element as HTMLInputElement).value).toBe('New Scenario')
  })

  describe('trace recorder', () => {
    it('calls recorder.start when Start Recording is clicked', async () => {
      const wrapper = mount(DevToolsView)

      const startBtn = wrapper.findAll('button').find((b) => b.text().includes('Start Recording'))
      expect(startBtn).toBeDefined()
      await startBtn!.trigger('click')
      await waitForUi()

      expect(recorderStart).toHaveBeenCalledWith('New Trace')
    })

    it('shows Stop Recording button while recording is active', async () => {
      recorderIsRecording.value = true
      recorderActionCount.value = 3

      const wrapper = mount(DevToolsView)

      expect(wrapper.text()).toContain('Stop Recording')
      expect(wrapper.text()).toContain('3 actions')
      expect(wrapper.text()).toContain('Recording in progress')
    })

    it('calls recorder.stop when Stop Recording is clicked', async () => {
      recorderIsRecording.value = true
      recorderActionCount.value = 2

      const wrapper = mount(DevToolsView)

      const stopBtn = wrapper.findAll('button').find((b) => b.text().includes('Stop Recording'))
      expect(stopBtn).toBeDefined()
      await stopBtn!.trigger('click')
      await waitForUi()

      expect(recorderStop).toHaveBeenCalledTimes(1)
    })

    it('adds completed traces to the trace list after stopping', async () => {
      const fakeTrace = {
        id: 'trace-1',
        name: 'My Captured Trace',
        actions: [{ type: 'click', label: 'Click button' }],
        durationMs: 1500,
        startedAt: new Date().toISOString(),
        stoppedAt: new Date().toISOString(),
      }
      recorderStop.mockReturnValue(fakeTrace)
      recorderIsRecording.value = true

      const wrapper = mount(DevToolsView)

      const stopBtn = wrapper.findAll('button').find((b) => b.text().includes('Stop Recording'))
      await stopBtn!.trigger('click')
      await waitForUi()

      expect(wrapper.text()).toContain('Recorded Traces')
      expect(wrapper.text()).toContain('My Captured Trace')
      expect(wrapper.text()).toContain('1 actions')
    })

    it('does not add to trace list when recorder.stop returns null', async () => {
      recorderStop.mockReturnValue(null)
      recorderIsRecording.value = true

      const wrapper = mount(DevToolsView)

      const stopBtn = wrapper.findAll('button').find((b) => b.text().includes('Stop Recording'))
      await stopBtn!.trigger('click')
      await waitForUi()

      expect(wrapper.text()).not.toContain('Recorded Traces')
    })
  })

  describe('scenario editor tab', () => {
    async function openScenarioTab(wrapper: ReturnType<typeof mount>) {
      const tab = wrapper.findAll('button').find((b) => b.text().includes('Scenario Editor'))
      await tab!.trigger('click')
      await waitForUi()
    }

    it('renders the blank scenario name in the editor input', async () => {
      const wrapper = mount(DevToolsView)
      await openScenarioTab(wrapper)

      const nameInput = wrapper.find('input[placeholder="Scenario name"]')
      expect(nameInput.exists()).toBe(true)
      expect((nameInput.element as HTMLInputElement).value).toBe('New Scenario')
    })

    it('renders step type buttons for adding steps', async () => {
      const wrapper = mount(DevToolsView)
      await openScenarioTab(wrapper)

      // Should have buttons for adding different step types (navigate, click, fill, etc.)
      const buttons = wrapper.findAll('button').map((b) => b.text())
      expect(
        buttons.some((t) => t.includes('navigate') || t.includes('click') || t.includes('fill')),
      ).toBe(true)
    })
  })
})
