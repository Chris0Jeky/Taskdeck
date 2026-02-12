import http from './http'
import type { User, CreateUserDto, UpdateUserDto } from '../types/users'

export const usersApi = {
  async getUsers(): Promise<User[]> {
    const { data } = await http.get<User[]>('/users')
    return data
  },

  async getUser(id: string): Promise<User> {
    const { data } = await http.get<User>(`/users/${id}`)
    return data
  },

  async getUserByUsername(username: string): Promise<User> {
    const { data } = await http.get<User>(`/users/by-username/${username}`)
    return data
  },

  async createUser(user: CreateUserDto): Promise<User> {
    const { data } = await http.post<User>('/users', user)
    return data
  },

  async updateUser(id: string, user: UpdateUserDto): Promise<User> {
    const { data } = await http.put<User>(`/users/${id}`, user)
    return data
  },

  async deactivateUser(id: string): Promise<void> {
    await http.post(`/users/${id}/deactivate`)
  },

  async activateUser(id: string): Promise<void> {
    await http.post(`/users/${id}/activate`)
  },
}
