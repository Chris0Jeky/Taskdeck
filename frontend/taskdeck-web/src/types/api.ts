export interface ApiError {
  errorCode: string
  message: string
}

export type AsyncStatus = 'idle' | 'loading' | 'success' | 'error'

export interface ApiState<T> {
  data: T | null
  status: AsyncStatus
  error: ApiError | null
}
