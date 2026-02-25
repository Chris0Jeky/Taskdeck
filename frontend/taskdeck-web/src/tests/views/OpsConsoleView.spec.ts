import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import OpsConsoleView from '../../views/OpsConsoleView.vue'

const mocks = vi.hoisted(() => ({
  getTemplates: vi.fn(),
  runCommand: vi.fn(),
  getRunLogs: vi.fn(),
  queryLogs: vi.fn(),
  getCorrelationLogs: vi.fn(),
  toastError: vi.fn(),
}))

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

  it('adds actionable role guidance when a restricted template run is forbidden', async () => {
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
    await runButton?.trigger('click')
    await waitForAsyncUi()

    expect(wrapper.text()).toContain("Template 'boards.list' requires role 'admin'")
    expect(wrapper.text()).toContain('Role context: you are signed in as Editor')
    expect(wrapper.text()).toContain('Need elevated access? Open Workspace > Settings')
    expect(mocks.toastError).toHaveBeenCalled()
  })
})
