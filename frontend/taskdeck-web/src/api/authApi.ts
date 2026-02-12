import http from './http'
import type { LoginRequest, RegisterRequest, ChangePasswordRequest, AuthResponse } from '../types/auth'

export const authApi = {
  async login(credentials: LoginRequest): Promise<AuthResponse> {
    const { data } = await http.post<AuthResponse>('/auth/login', credentials)
    return data
  },

  async register(request: RegisterRequest): Promise<AuthResponse> {
    const { data } = await http.post<AuthResponse>('/auth/register', request)
    return data
  },

  async changePassword(request: ChangePasswordRequest): Promise<void> {
    await http.post('/auth/change-password', request)
  },
}
