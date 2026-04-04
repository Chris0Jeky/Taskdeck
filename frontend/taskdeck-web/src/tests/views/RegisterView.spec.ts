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

async function waitForUi() {
  await Promise.resolve()
  await Promise.resolve()
}

describe('RegisterView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    sessionMock.error = null
    sessionMock.register.mockResolvedValue(undefined)
  })

  it('renders the create account title', () => {
    const wrapper = mount(RegisterView)
    expect(wrapper.text()).toContain('Create an account')
  })

  it('renders all required form fields', () => {
    const wrapper = mount(RegisterView)
    expect(wrapper.find('#reg-username').exists()).toBe(true)
    expect(wrapper.find('#reg-email').exists()).toBe(true)
    expect(wrapper.find('#reg-password').exists()).toBe(true)
    expect(wrapper.find('#reg-confirm').exists()).toBe(true)
  })

  it('shows error when submitting with empty fields', async () => {
    const wrapper = mount(RegisterView)

    await wrapper.find('form').trigger('submit')
    await waitForUi()

    expect(wrapper.find('[role="alert"]').text()).toContain('Please fill in all fields.')
    expect(sessionMock.register).not.toHaveBeenCalled()
  })

  it('shows error when passwords do not match', async () => {
    const wrapper = mount(RegisterView)

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
    const wrapper = mount(RegisterView)

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
    const wrapper = mount(RegisterView)

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

    const wrapper = mount(RegisterView)

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

    const wrapper = mount(RegisterView)

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

    const wrapper = mount(RegisterView)

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
    const wrapper = mount(RegisterView)

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
})
