export interface LoginRequest {
  username: string
  password: string
}

export interface RegisterRequest {
  username: string
  email: string
  password: string
}

export interface ChangePasswordRequest {
  userId: string
  currentPassword: string
  newPassword: string
}

export interface AuthResponse {
  token: string
  userId: string
  username: string
  email: string
}

export interface SessionState {
  token: string | null
  userId: string | null
  username: string | null
  email: string | null
  isAuthenticated: boolean
  expiresAt: string | null
}
