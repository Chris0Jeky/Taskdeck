import axios, { AxiosHeaders, type InternalAxiosRequestConfig } from 'axios'
import { isTokenExpired } from '../utils/jwt'
import { createRequestId } from '../utils/requestId'
import { isAuthRoutePath } from '../utils/navigation'
import { isDemoMode } from '../utils/demoMode'
import * as tokenStorage from '../utils/tokenStorage'

const REQUEST_ID_HEADER = 'X-Request-Id'

function ensureRequestIdHeader(config: InternalAxiosRequestConfig): void {
  const headers = AxiosHeaders.from(config.headers)
  if (!headers.get(REQUEST_ID_HEADER)) {
    headers.set(REQUEST_ID_HEADER, createRequestId())
  }
  config.headers = headers
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

    const token = tokenStorage.getToken()
    if (token) {
      if (isTokenExpired(token)) {
        tokenStorage.clearAll()
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

      // Handle 401 - clear session and redirect to login (skip in demo mode)
      if (error.response.status === 401 && !isDemoMode) {
        tokenStorage.clearAll()
        const pathname = window.location.pathname
        const currentPath = `${pathname}${window.location.search}`
        if (!isAuthRoutePath(pathname)) {
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
