import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import MfaSetup from '../../components/MfaSetup.vue'

const authApiMock = vi.hoisted(() => ({
  getMfaStatus: vi.fn(),
  setupMfa: vi.fn(),
  confirmMfa: vi.fn(),
  disableMfa: vi.fn(),
}))

vi.mock('../../api/authApi', () => ({
  authApi: authApiMock,
}))

vi.mock('../../utils/errorMessage', () => ({
  getErrorMessage: (error: unknown, fallback: string) =>
    error instanceof Error && error.message ? error.message : fallback,
}))

async function waitForUi() {
  await Promise.resolve()
  await Promise.resolve()
}

describe('MfaSetup', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    authApiMock.getMfaStatus.mockResolvedValue({
      isEnabled: false,
      isSetupAvailable: true,
    })
    authApiMock.setupMfa.mockResolvedValue({
      sharedSecret: 'ABCDEF123456',
      qrCodeUri: 'otpauth://totp/Taskdeck:testuser?secret=ABCDEF123456&issuer=Taskdeck',
      recoveryCodes: ['1111-2222', '3333-4444'],
    })
  })

  it('shows shared-secret guidance instead of claiming a QR code is rendered', async () => {
    const wrapper = mount(MfaSetup)
    await waitForUi()

    await wrapper.find('button').trigger('click')
    await waitForUi()

    expect(wrapper.text()).toContain('Add this shared secret to your authenticator app')
    expect(wrapper.text()).toContain('Show provisioning URI')
    expect(wrapper.text()).not.toContain('Scan the QR code below')
  })
})
