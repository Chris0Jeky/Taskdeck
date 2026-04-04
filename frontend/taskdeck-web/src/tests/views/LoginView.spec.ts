import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { reactive, ref } from 'vue'
import LoginView from '../../views/LoginView.vue'

// Hoist route/router mocks so they're available to vi.mock factories
const routerMocks = vi.hoisted(() => ({
  push: vi.fn(),
  replace: vi.fn(),
}))

const routeMock = vi.hoisted(() => ({
  query: {} as Record<string, unknown>,
  path: '/login',
}))

const sessionMock = vi.hoisted(() => ({
  login: vi.fn(),
  loginAsDemo: vi.fn(),
  register: vi.fn(),
  exchangeOAuthCode: vi.fn(),
  error: null as string | null,
}))

const isDemoModeMock = vi.hoisted(() => ({ value: false }))

vi.mock('vue-router', () => ({
  useRouter: () => ({
    push: routerMocks.push,
    replace: routerMocks.replace,
  }),
  useRoute: () => routeMock,
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
    getProviders: vi.fn().mockResolvedValue({ gitHub: false }),
  },
}))

vi.mock('../../utils/navigation', () => ({
  sanitizeInternalRedirect: (url: string) => url,
}))

vi.mock('../../utils/demoMode', () => ({
  get isDemoMode() {
    return isDemoModeMock.value
  },
}))

async function waitForUi() {
  await Promise.resolve()
  await Promise.resolve()
}

describe('LoginView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    routeMock.query = {}
    routeMock.path = '/login'
    sessionMock.error = null
    isDemoModeMock.value = false
    sessionMock.login.mockResolvedValue(undefined)
    sessionMock.loginAsDemo.mockReturnValue(undefined)
    sessionMock.exchangeOAuthCode.mockResolvedValue(undefined)
  })

  it('renders the sign-in title', async () => {
    const wrapper = mount(LoginView)
    await waitForUi()

    expect(wrapper.text()).toContain('Sign in to Taskdeck')
  })

  it('renders username and password inputs', async () => {
    const wrapper = mount(LoginView)
    await waitForUi()

    expect(wrapper.find('#login-username').exists()).toBe(true)
    expect(wrapper.find('#login-password').exists()).toBe(true)
  })

  it('shows validation error when submitting with empty fields', async () => {
    const wrapper = mount(LoginView)
    await waitForUi()

    await wrapper.find('form').trigger('submit')
    await waitForUi()

    expect(wrapper.text()).toContain('Please enter both username and password.')
    expect(sessionMock.login).not.toHaveBeenCalled()
  })

  it('shows validation error when only username is provided', async () => {
    const wrapper = mount(LoginView)
    await waitForUi()

    await wrapper.find('#login-username').setValue('alice')
    await wrapper.find('form').trigger('submit')
    await waitForUi()

    expect(wrapper.text()).toContain('Please enter both username and password.')
    expect(sessionMock.login).not.toHaveBeenCalled()
  })

  it('calls session.login and navigates on successful submit', async () => {
    const wrapper = mount(LoginView)
    await waitForUi()

    await wrapper.find('#login-username').setValue('alice')
    await wrapper.find('#login-password').setValue('secret123')
    await wrapper.find('form').trigger('submit')
    await waitForUi()

    expect(sessionMock.login).toHaveBeenCalledWith({
      usernameOrEmail: 'alice',
      password: 'secret123',
    })
    expect(routerMocks.push).toHaveBeenCalledWith('/workspace/home')
  })

  it('respects the redirect query param when navigating after login', async () => {
    routeMock.query = { redirect: '/workspace/boards/board-1' }

    const wrapper = mount(LoginView)
    await waitForUi()

    await wrapper.find('#login-username').setValue('alice')
    await wrapper.find('#login-password').setValue('secret123')
    await wrapper.find('form').trigger('submit')
    await waitForUi()

    expect(routerMocks.push).toHaveBeenCalledWith('/workspace/boards/board-1')
  })

  it('shows error message when login fails', async () => {
    sessionMock.login.mockRejectedValue(new Error('invalid credentials'))
    sessionMock.error = 'Invalid username or password'

    const wrapper = mount(LoginView)
    await waitForUi()

    await wrapper.find('#login-username').setValue('alice')
    await wrapper.find('#login-password').setValue('wrongpass')
    await wrapper.find('form').trigger('submit')
    await waitForUi()

    expect(wrapper.find('[role="alert"]').text()).toContain('Invalid username or password')
  })

  it('shows fallback error message when session.error is null after login failure', async () => {
    sessionMock.login.mockRejectedValue(new Error('network'))
    sessionMock.error = null

    const wrapper = mount(LoginView)
    await waitForUi()

    await wrapper.find('#login-username').setValue('alice')
    await wrapper.find('#login-password').setValue('pass')
    await wrapper.find('form').trigger('submit')
    await waitForUi()

    expect(wrapper.find('[role="alert"]').text()).toContain('Login failed. Please try again.')
  })

  it('disables submit button while submitting', async () => {
    let resolveLogin!: () => void
    sessionMock.login.mockReturnValue(
      new Promise<void>((res) => {
        resolveLogin = res
      }),
    )

    const wrapper = mount(LoginView)
    await waitForUi()

    await wrapper.find('#login-username').setValue('alice')
    await wrapper.find('#login-password').setValue('pass')
    const submitBtn = wrapper.find('button[type="submit"]')
    await wrapper.find('form').trigger('submit')

    // While the login promise is pending the button should be disabled
    expect(submitBtn.attributes('disabled')).toBeDefined()
    expect(submitBtn.text()).toContain('Signing in...')

    resolveLogin()
    await waitForUi()
    await waitForUi()

    expect(submitBtn.attributes('disabled')).toBeUndefined()
  })

  describe('demo mode', () => {
    it('renders the Enter Demo button and hides the login form', async () => {
      isDemoModeMock.value = true

      const wrapper = mount(LoginView)
      await waitForUi()

      expect(wrapper.text()).toContain('Enter Demo')
      expect(wrapper.find('form').exists()).toBe(false)
    })

    it('calls loginAsDemo and navigates when Enter Demo is clicked', async () => {
      isDemoModeMock.value = true

      const wrapper = mount(LoginView)
      await waitForUi()

      const demoBtn = wrapper.findAll('button').find((b) => b.text().includes('Enter Demo'))
      expect(demoBtn).toBeDefined()
      await demoBtn!.trigger('click')
      await waitForUi()

      expect(sessionMock.loginAsDemo).toHaveBeenCalled()
      expect(routerMocks.push).toHaveBeenCalledWith('/workspace/home')
    })
  })
})
