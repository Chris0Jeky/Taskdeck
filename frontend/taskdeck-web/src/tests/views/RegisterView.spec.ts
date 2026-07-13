import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import RegisterView from '../../views/RegisterView.vue'

const routerMocks = vi.hoisted(() => ({
  push: vi.fn(),
}))

const sessionMock = vi.hoisted(() => ({
  register: vi.fn(),
  error: null as string | null,
}))

const authApiMock = vi.hoisted(() => ({
  getProviders: vi.fn(),
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({
    push: routerMocks.push,
  }),
  RouterLink: {
    template: '<a><slot /></a>',
    props: ['to'],
  },
}))

vi.mock('../../store/sessionStore', () => ({
  useSessionStore: () => sessionMock,
}))

vi.mock('../../api/authApi', () => ({
  authApi: {
    getProviders: authApiMock.getProviders,
  },
}))

async function waitForUi() {
  await Promise.resolve()
  await Promise.resolve()
}

async function mountView() {
  const wrapper = mount(RegisterView)
  await waitForUi()
  return wrapper
}

describe('RegisterView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    sessionMock.error = null
    sessionMock.register.mockResolvedValue(undefined)
    authApiMock.getProviders.mockResolvedValue({
      gitHub: false,
      oidc: [],
      registration: {
        mode: 'Open',
        isRegistrationAvailable: true,
        inviteRequired: false,
      },
    })
  })

  it('renders the create account title', async () => {
    const wrapper = await mountView()
    expect(wrapper.text()).toContain('Create an account')
  })

  it('renders all required form fields', async () => {
    const wrapper = await mountView()
    expect(wrapper.find('#reg-username').exists()).toBe(true)
    expect(wrapper.find('#reg-email').exists()).toBe(true)
    expect(wrapper.find('#reg-password').exists()).toBe(true)
    expect(wrapper.find('#reg-confirm').exists()).toBe(true)
  })

  it('shows error when submitting with empty fields', async () => {
    const wrapper = await mountView()

    await wrapper.find('form').trigger('submit')
    await waitForUi()

    expect(wrapper.find('[role="alert"]').text()).toContain('Please fill in all fields.')
    expect(sessionMock.register).not.toHaveBeenCalled()
  })

  it('shows error when passwords do not match', async () => {
    const wrapper = await mountView()

    await wrapper.find('#reg-username').setValue('alice')
    await wrapper.find('#reg-email').setValue('alice@example.com')
    await wrapper.find('#reg-password').setValue('password1')
    await wrapper.find('#reg-confirm').setValue('password2')
    await wrapper.find('form').trigger('submit')
    await waitForUi()

    expect(wrapper.find('[role="alert"]').text()).toContain('Passwords do not match.')
    expect(sessionMock.register).not.toHaveBeenCalled()
  })

  it('shows error when password is too short', async () => {
    const wrapper = await mountView()

    await wrapper.find('#reg-username').setValue('alice')
    await wrapper.find('#reg-email').setValue('alice@example.com')
    await wrapper.find('#reg-password').setValue('abc')
    await wrapper.find('#reg-confirm').setValue('abc')
    await wrapper.find('form').trigger('submit')
    await waitForUi()

    expect(wrapper.find('[role="alert"]').text()).toContain('Password must be at least 6 characters.')
    expect(sessionMock.register).not.toHaveBeenCalled()
  })

  it('calls session.register with correct payload and navigates on success', async () => {
    const wrapper = await mountView()

    await wrapper.find('#reg-username').setValue('alice')
    await wrapper.find('#reg-email').setValue('alice@example.com')
    await wrapper.find('#reg-password').setValue('securePass1')
    await wrapper.find('#reg-confirm').setValue('securePass1')
    await wrapper.find('form').trigger('submit')
    await waitForUi()

    expect(sessionMock.register).toHaveBeenCalledWith({
      username: 'alice',
      email: 'alice@example.com',
      password: 'securePass1',
    })
    expect(routerMocks.push).toHaveBeenCalledWith('/workspace/home')
  })

  it('shows error message on registration failure', async () => {
    sessionMock.register.mockRejectedValue(new Error('username taken'))
    sessionMock.error = 'Username already exists'

    const wrapper = await mountView()

    await wrapper.find('#reg-username').setValue('alice')
    await wrapper.find('#reg-email').setValue('alice@example.com')
    await wrapper.find('#reg-password').setValue('securePass1')
    await wrapper.find('#reg-confirm').setValue('securePass1')
    await wrapper.find('form').trigger('submit')
    await waitForUi()

    expect(wrapper.find('[role="alert"]').text()).toContain('Username already exists')
    expect(routerMocks.push).not.toHaveBeenCalled()
  })

  it('shows fallback error when session.error is null after registration failure', async () => {
    sessionMock.register.mockRejectedValue(new Error('network'))
    sessionMock.error = null

    const wrapper = await mountView()

    await wrapper.find('#reg-username').setValue('alice')
    await wrapper.find('#reg-email').setValue('alice@example.com')
    await wrapper.find('#reg-password').setValue('securePass1')
    await wrapper.find('#reg-confirm').setValue('securePass1')
    await wrapper.find('form').trigger('submit')
    await waitForUi()

    expect(wrapper.find('[role="alert"]').text()).toContain('Registration failed. Please try again.')
  })

  it('disables submit button while submitting', async () => {
    let resolveRegister!: () => void
    sessionMock.register.mockReturnValue(
      new Promise<void>((res) => {
        resolveRegister = res
      }),
    )

    const wrapper = await mountView()

    await wrapper.find('#reg-username').setValue('alice')
    await wrapper.find('#reg-email').setValue('alice@example.com')
    await wrapper.find('#reg-password').setValue('securePass1')
    await wrapper.find('#reg-confirm').setValue('securePass1')
    const submitBtn = wrapper.find('button[type="submit"]')
    await wrapper.find('form').trigger('submit')

    expect(submitBtn.attributes('disabled')).toBeDefined()
    expect(submitBtn.text()).toContain('Creating account...')

    resolveRegister()
    await waitForUi()
    await waitForUi()

    expect(submitBtn.attributes('disabled')).toBeUndefined()
  })

  it('trims username and email before submitting', async () => {
    const wrapper = await mountView()

    await wrapper.find('#reg-username').setValue('  alice  ')
    await wrapper.find('#reg-email').setValue('  alice@example.com  ')
    await wrapper.find('#reg-password').setValue('securePass1')
    await wrapper.find('#reg-confirm').setValue('securePass1')
    await wrapper.find('form').trigger('submit')
    await waitForUi()

    expect(sessionMock.register).toHaveBeenCalledWith({
      username: 'alice',
      email: 'alice@example.com',
      password: 'securePass1',
    })
  })

  it('requires and submits an operator invite in InviteOnly mode', async () => {
    authApiMock.getProviders.mockResolvedValue({
      gitHub: false,
      oidc: [],
      registration: {
        mode: 'InviteOnly',
        isRegistrationAvailable: true,
        inviteRequired: true,
      },
    })
    const wrapper = await mountView()

    await wrapper.find('#reg-username').setValue('alice')
    await wrapper.find('#reg-email').setValue('alice@example.com')
    await wrapper.find('#reg-password').setValue('securePass1')
    await wrapper.find('#reg-confirm').setValue('securePass1')
    await wrapper.find('#reg-invite').setValue('  tdi_OperatorCode  ')
    await wrapper.find('form').trigger('submit')
    await waitForUi()

    expect(sessionMock.register).toHaveBeenCalledWith({
      username: 'alice',
      email: 'alice@example.com',
      password: 'securePass1',
      inviteCode: 'tdi_OperatorCode',
    })
  })

  it('hides the form when registration is closed after bootstrap', async () => {
    authApiMock.getProviders.mockResolvedValue({
      gitHub: false,
      oidc: [],
      registration: {
        mode: 'Closed',
        isRegistrationAvailable: false,
        inviteRequired: false,
      },
    })

    const wrapper = await mountView()

    expect(wrapper.find('form').exists()).toBe(false)
    expect(wrapper.text()).toContain('Registration is closed')
  })

  it('fails closed when registration availability cannot be loaded', async () => {
    authApiMock.getProviders.mockRejectedValue(new Error('unreachable'))

    const wrapper = await mountView()

    expect(wrapper.find('form').exists()).toBe(false)
    expect(wrapper.get('[role="alert"]').text()).toContain('Could not check whether')
    expect(wrapper.text()).toContain('Try again')
  })

  it('fails closed when the registration field is missing from the provider payload', async () => {
    authApiMock.getProviders.mockResolvedValue({ gitHub: false, oidc: [] })

    const wrapper = await mountView()

    expect(wrapper.find('form').exists()).toBe(false)
    expect(wrapper.get('[role="alert"]').text()).toContain('Could not check whether')
  })

  it('fails closed when the registration field is null', async () => {
    authApiMock.getProviders.mockResolvedValue({ gitHub: false, oidc: [], registration: null })

    const wrapper = await mountView()

    expect(wrapper.find('form').exists()).toBe(false)
    expect(wrapper.get('[role="alert"]').text()).toContain('Could not check whether')
  })

  it('fails closed when the registration payload is malformed', async () => {
    // Missing `isRegistrationAvailable`/`inviteRequired` and an unknown mode: an older
    // or corrupted response must not be trusted to render a live form.
    authApiMock.getProviders.mockResolvedValue({
      gitHub: false,
      oidc: [],
      registration: { mode: 'Sometimes' },
    })

    const wrapper = await mountView()

    expect(wrapper.find('form').exists()).toBe(false)
    expect(wrapper.get('[role="alert"]').text()).toContain('Could not check whether')
    // The submit button never appears, so there is no dead form to submit.
    expect(wrapper.find('button[type="submit"]').exists()).toBe(false)
  })

  it('recovers to a live form when Try again succeeds after a malformed payload', async () => {
    authApiMock.getProviders.mockResolvedValueOnce({
      gitHub: false,
      oidc: [],
      registration: { mode: 'Sometimes' },
    })

    const wrapper = await mountView()
    expect(wrapper.find('form').exists()).toBe(false)

    authApiMock.getProviders.mockResolvedValue({
      gitHub: false,
      oidc: [],
      registration: { mode: 'Open', isRegistrationAvailable: true, inviteRequired: false },
    })
    await wrapper.get('[role="alert"] button').trigger('click')
    await waitForUi()

    expect(wrapper.find('form').exists()).toBe(true)
  })
})
