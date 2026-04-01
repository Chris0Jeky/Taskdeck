import http from './http'
import type { LoginRequest, RegisterRequest, ChangePasswordRequest, AuthResponse, AuthProviders } from '../types/auth'

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

  async getProviders(): Promise<AuthProviders> {
    const { data } = await http.get<AuthProviders>('/auth/providers')
    return data
  },

  async exchangeOAuthCode(code: string): Promise<AuthResponse> {
    const { data } = await http.post<AuthResponse>('/auth/github/exchange', { code })
    return data
  },
}
