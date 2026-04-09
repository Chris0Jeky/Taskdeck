export interface LoginRequest {
  usernameOrEmail: string
  password: string
}

export interface RegisterRequest {
  username: string
  email: string
  password: string
}

export interface ChangePasswordRequest {
  currentPassword: string
  newPassword: string
}

export interface AuthResponse {
  token: string
  user: AuthUser
}

export interface AuthUser {
  id: string
  username: string
  email: string
  defaultRole: number
  isActive: boolean
  createdAt: string
  updatedAt: string
}

export interface SessionState {
  token: string | null
  userId: string | null
  username: string | null
  email: string | null
  defaultRole: number | null
  isAuthenticated: boolean
  expiresAt: string | null
}

export interface AuthProviders {
  gitHub: boolean
}

export interface LinkedAccount {
  provider: string
  providerUserId: string
  displayName: string | null
  avatarUrl: string | null
  linkedAt: string
}
