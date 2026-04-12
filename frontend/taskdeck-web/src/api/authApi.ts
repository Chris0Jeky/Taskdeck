import http from './http'
import type { LoginRequest, RegisterRequest, ChangePasswordRequest, AuthResponse, AuthProviders, LinkedAccount } from '../types/auth'

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

  async getLinkedAccounts(): Promise<LinkedAccount[]> {
    const { data } = await http.get<LinkedAccount[]>('/auth/linked-accounts')
    return data
  },

  async linkGitHub(code: string): Promise<LinkedAccount> {
    const { data } = await http.post<LinkedAccount>('/auth/github/link', { code })
    return data
  },

  async unlinkGitHub(): Promise<void> {
    await http.delete('/auth/github/link')
  },
}
