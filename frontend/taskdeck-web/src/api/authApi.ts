import http from './http'
import type {
  LoginRequest,
  RegisterRequest,
  ChangePasswordRequest,
  AuthResponse,
  AuthProviders,
  MfaStatus,
  MfaSetupResponse,
  MfaVerifyRequest,
} from '../types/auth'

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

  async exchangeOidcCode(code: string): Promise<AuthResponse> {
    const { data } = await http.post<AuthResponse>('/auth/oidc/exchange', { code })
    return data
  },

  // MFA endpoints
  async getMfaStatus(): Promise<MfaStatus> {
    const { data } = await http.get<MfaStatus>('/auth/mfa/status')
    return data
  },

  async setupMfa(): Promise<MfaSetupResponse> {
    const { data } = await http.post<MfaSetupResponse>('/auth/mfa/setup')
    return data
  },

  async confirmMfa(request: MfaVerifyRequest): Promise<void> {
    await http.post('/auth/mfa/confirm', request)
  },

  async verifyMfa(request: MfaVerifyRequest): Promise<void> {
    await http.post('/auth/mfa/verify', request)
  },

  async disableMfa(request: MfaVerifyRequest): Promise<void> {
    await http.post('/auth/mfa/disable', request)
  },
}
