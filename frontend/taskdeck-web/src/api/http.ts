import axios, { type InternalAxiosRequestConfig } from 'axios'
import { isTokenExpired } from '../utils/jwt'
import { createRequestId } from '../utils/requestId'

const TOKEN_KEY = 'taskdeck_token'
const SESSION_KEY = 'taskdeck_session'
const REQUEST_ID_HEADER = 'X-Request-Id'

function ensureRequestIdHeader(config: InternalAxiosRequestConfig): void {
  const headers = config.headers
  const hasExisting = (() => {
    if (!headers) return false
    if (typeof (headers as { get?: unknown }).get === 'function') {
      const get = (headers as { get: (name: string) => string | undefined }).get
      return !!get(REQUEST_ID_HEADER)
    }
    const plain = headers as Record<string, unknown>
    return typeof plain[REQUEST_ID_HEADER] === 'string' || typeof plain['x-request-id'] === 'string'
  })()

  if (hasExisting) return

  if (headers && typeof (headers as { set?: unknown }).set === 'function') {
    const set = (headers as { set: (name: string, value: string) => void }).set
    set(REQUEST_ID_HEADER, createRequestId())
    return
  }

  config.headers = {
    ...(headers ?? {}),
    [REQUEST_ID_HEADER]: createRequestId(),
  }
}

const http = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000/api',
  headers: {
    'Content-Type': 'application/json',
  },
})

// Request interceptor for auth token
http.interceptors.request.use(
  (config) => {
    ensureRequestIdHeader(config)

    const token = localStorage.getItem(TOKEN_KEY)
    if (token) {
      if (isTokenExpired(token)) {
        localStorage.removeItem(TOKEN_KEY)
        localStorage.removeItem(SESSION_KEY)
      } else {
        config.headers.Authorization = `Bearer ${token}`
      }
    }
    return config
  },
  (error) => Promise.reject(error)
)

// Response interceptor for error handling
http.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response) {
      console.error('API Error:', error.response.data)

      // Handle 401 - clear session and redirect to login
      if (error.response.status === 401) {
        localStorage.removeItem(TOKEN_KEY)
        localStorage.removeItem(SESSION_KEY)
        const currentPath = `${window.location.pathname}${window.location.search}`
        if (currentPath !== '/login' && currentPath !== '/register') {
          window.location.href = `/login?redirect=${encodeURIComponent(currentPath)}`
        }
      }
    } else if (error.request) {
      console.error('Network Error:', error.message)
    } else {
      console.error('Error:', error.message)
    }
    return Promise.reject(error)
  }
)

export default http
