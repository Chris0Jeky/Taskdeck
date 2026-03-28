import { beforeEach, describe, expect, it, vi } from 'vitest'
import http from '../../api/http'
import { usersApi } from '../../api/usersApi'

vi.mock('../../api/http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
  },
}))

describe('usersApi', () => {
  const userPayload = {
    id: 'user-1',
    username: 'alice',
    email: 'alice@example.com',
    defaultRole: 2,
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('gets all users', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: [userPayload] })

    await expect(usersApi.getUsers()).resolves.toEqual([userPayload])

    expect(http.get).toHaveBeenCalledWith('/users')
  })

  it('gets a single user by id', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: userPayload })

    await expect(usersApi.getUser('user-1')).resolves.toEqual(userPayload)

    expect(http.get).toHaveBeenCalledWith('/users/user-1')
  })

  it('gets a single user by username', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: userPayload })

    await expect(usersApi.getUserByUsername('alice')).resolves.toEqual(userPayload)

    expect(http.get).toHaveBeenCalledWith('/users/by-username/alice')
  })

  it('creates a user', async () => {
    const request = {
      username: 'alice',
      email: 'alice@example.com',
      password: 'super-secret',
    }
    vi.mocked(http.post).mockResolvedValue({ data: userPayload })

    await expect(usersApi.createUser(request)).resolves.toEqual(userPayload)

    expect(http.post).toHaveBeenCalledWith('/users', request)
  })

  it('updates a user', async () => {
    const request = {
      username: 'alice-updated',
      email: 'alice-updated@example.com',
    }
    const updatedUser = {
      ...userPayload,
      ...request,
      updatedAt: '2026-01-02T00:00:00Z',
    }
    vi.mocked(http.put).mockResolvedValue({ data: updatedUser })

    await expect(usersApi.updateUser('user-1', request)).resolves.toEqual(updatedUser)

    expect(http.put).toHaveBeenCalledWith('/users/user-1', request)
  })

  it('deactivates a user', async () => {
    vi.mocked(http.post).mockResolvedValue({ data: undefined })

    await usersApi.deactivateUser('user-1')

    expect(http.post).toHaveBeenCalledWith('/users/user-1/deactivate')
  })

  it('activates a user', async () => {
    vi.mocked(http.post).mockResolvedValue({ data: undefined })

    await usersApi.activateUser('user-1')

    expect(http.post).toHaveBeenCalledWith('/users/user-1/activate')
  })
})
