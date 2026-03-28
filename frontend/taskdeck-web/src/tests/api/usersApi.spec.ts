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

    await expect(usersApi.getUser(userPayload.id)).resolves.toEqual(userPayload)

    expect(http.get).toHaveBeenCalledWith(`/users/${userPayload.id}`)
  })

  it('gets a single user by username', async () => {
    vi.mocked(http.get).mockResolvedValue({ data: userPayload })

    await expect(usersApi.getUserByUsername(userPayload.username)).resolves.toEqual(userPayload)

    expect(http.get).toHaveBeenCalledWith(`/users/by-username/${userPayload.username}`)
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

    expect(http.put).toHaveBeenCalledWith(`/users/${userPayload.id}`, request)
  })

  it('deactivates a user', async () => {
    vi.mocked(http.post).mockResolvedValue({ data: undefined })

    await usersApi.deactivateUser(userPayload.id)

    expect(http.post).toHaveBeenCalledWith(`/users/${userPayload.id}/deactivate`)
  })

  it('activates a user', async () => {
    vi.mocked(http.post).mockResolvedValue({ data: undefined })

    await usersApi.activateUser(userPayload.id)

    expect(http.post).toHaveBeenCalledWith(`/users/${userPayload.id}/activate`)
  })

  it('propagates HTTP errors from getUsers', async () => {
    const error = new Error('Network Error')
    vi.mocked(http.get).mockRejectedValue(error)

    await expect(usersApi.getUsers()).rejects.toThrow('Network Error')
  })

  it('propagates HTTP errors from createUser', async () => {
    const error = new Error('Server Error')
    vi.mocked(http.post).mockRejectedValue(error)

    await expect(
      usersApi.createUser({ username: 'a', email: 'a@b.c', password: 'x' }),
    ).rejects.toThrow('Server Error')
  })
})
