import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive } from 'vue'
import OpsConsoleView from '../../views/OpsConsoleView.vue'

const mocks = vi.hoisted(() => ({
  getTemplates: vi.fn(),
  runCommand: vi.fn(),
  getRunLogs: vi.fn(),
  queryLogs: vi.fn(),
  getCorrelationLogs: vi.fn(),
  toastError: vi.fn(),
  routerPush: vi.fn(),
}))

const mockRoute = reactive({
  name: 'workspace-ops-cli',
  path: '/workspace/ops/cli',
})

vi.mock('../../api/opsApi', () => ({
  opsApi: {
    getTemplates: mocks.getTemplates,
    runCommand: mocks.runCommand,
    getRunLogs: mocks.getRunLogs,
    queryLogs: mocks.queryLogs,
    getCorrelationLogs: mocks.getCorrelationLogs,
  },
}))

vi.mock('../../api/http', () => ({
  default: {
    request: vi.fn(),
  },
}))

vi.mock('vue-router', () => ({
  useRoute: () => mockRoute,
  useRouter: () => ({
    push: mocks.routerPush,
  }),
}))

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: () => ({
    defaultRole: 2,
  }),
}))

vi.mock('../../store/toastStore', () => ({
  useToastStore: () => ({
    error: mocks.toastError,
    success: vi.fn(),
    info: vi.fn(),
  }),
}))

vi.mock('../../composables/useErrorMapper', () => ({
  getErrorDisplay: (error: unknown, fallback: string) => {
    const maybeResponse = error as { response?: { data?: { errorCode?: string; message?: string } } }
    const apiError = maybeResponse.response?.data
    if (apiError?.message) {
      return {
        message: apiError.message,
        code: apiError.errorCode ?? null,
      }
    }

    return { message: fallback, code: null }
  },
}))

async function waitForAsyncUi() {
  await Promise.resolve()
  await Promise.resolve()
}

function buildTemplate(
  name: string,
  requiredRole: string,
): {
  name: string
  description: string
  riskClass: string
  timeoutSeconds: number
  requiredRole: string
  acceptedParameters: string[]
} {
  return {
    name,
    description: `${name} description`,
    riskClass: 'ReadOnly',
    timeoutSeconds: 30,
    requiredRole,
    acceptedParameters: [],
  }
}

describe('OpsConsoleView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockRoute.name = 'workspace-ops-cli'
    mockRoute.path = '/workspace/ops/cli'
    mocks.getRunLogs.mockResolvedValue([])
    mocks.queryLogs.mockResolvedValue([])
    mocks.getCorrelationLogs.mockResolvedValue([])
  })

  it('shows current role context and runnable template discoverability', async () => {
    mocks.getTemplates.mockResolvedValue([
      buildTemplate('boards.list', 'admin'),
      buildTemplate('health.check', 'editor'),
    ])

    const wrapper = mount(OpsConsoleView, {
      global: {
        stubs: {
          InputAssistField: true,
        },
      },
    })

    await waitForAsyncUi()

    expect(wrapper.text()).toContain('Current role: Editor')
    expect(wrapper.text()).toContain('Runnable templates: health.check')
    expect(wrapper.text()).toContain('Restricted templates require a higher role')
    expect(wrapper.text()).toContain('Access: Runnable for your role')
  })

  it('shows backend forbidden guidance without duplicating client-side guidance', async () => {
    mocks.getTemplates.mockResolvedValue([
      buildTemplate('boards.list', 'admin'),
    ])

    mocks.runCommand.mockRejectedValue({
      response: {
        data: {
          errorCode: 'Forbidden',
          message:
            "Template 'boards.list' requires role 'admin'. Your current role is 'editor'. Runnable templates for your role: none. Next step: open Workspace > Settings to confirm your account role, then ask an owner/admin to assign elevated access if needed.",
        },
      },
    })

    const wrapper = mount(OpsConsoleView, {
      global: {
        stubs: {
          InputAssistField: true,
        },
      },
    })

    await waitForAsyncUi()

    const runButton = wrapper
      .findAll('button')
      .find((button) => button.text().includes('Run Template'))
    expect(runButton).toBeDefined()
    await runButton!.trigger('click')
    await waitForAsyncUi()

    expect(wrapper.text()).toContain("Template 'boards.list' requires role 'admin'")
    expect(wrapper.text()).toContain('Next step: open Workspace > Settings')
    expect(wrapper.text()).not.toContain('Role context: you are signed in as Editor')
    expect(wrapper.text()).not.toContain('Need elevated access? Open Workspace > Settings and follow the operator role-assignment guidance.')
    expect(mocks.toastError).toHaveBeenCalled()
  })

  it('respects the logs route as the initial active tab and loads logs immediately', async () => {
    mockRoute.name = 'workspace-ops-logs'
    mocks.getTemplates.mockResolvedValue([
      buildTemplate('health.check', 'editor'),
    ])

    const wrapper = mount(OpsConsoleView, {
      global: {
        stubs: {
          InputAssistField: true,
        },
      },
    })

    await waitForAsyncUi()

    expect(mocks.queryLogs).toHaveBeenCalledWith({
      level: undefined,
      source: undefined,
      limit: 200,
    })
    expect(wrapper.text()).toContain('No logs match the current filters')
  })

  it('navigates to the logs route when the logs tab is selected', async () => {
    mocks.getTemplates.mockResolvedValue([
      buildTemplate('health.check', 'editor'),
    ])

    const wrapper = mount(OpsConsoleView, {
      global: {
        stubs: {
          InputAssistField: true,
        },
      },
    })

    await waitForAsyncUi()

    const logsTab = wrapper.findAll('button').find((button) => button.text().trim() === 'Logs')
    expect(logsTab).toBeTruthy()

    await logsTab!.trigger('click')

    expect(mocks.routerPush).toHaveBeenCalledWith('/workspace/ops/logs')
  })
})
