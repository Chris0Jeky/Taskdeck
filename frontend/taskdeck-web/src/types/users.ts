export interface User {
  id: string
  username: string
  email: string
  isActive: boolean
  createdAt: string
  updatedAt: string
}

export interface CreateUserDto {
  username: string
  email: string
  password: string
}

export interface UpdateUserDto {
  username?: string | null
  email?: string | null
}
