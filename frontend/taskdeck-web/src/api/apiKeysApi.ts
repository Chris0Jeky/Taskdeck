import http from './http'

export interface ApiKeyListItem {
  id: string
  keyPrefix: string
  name: string
  createdAt: string
  expiresAt: string | null
  revokedAt: string | null
  lastUsedAt: string | null
  isActive: boolean
}

export interface CreateApiKeyResponse {
  id: string
  key: string
  keyPrefix: string
  name: string
  createdAt: string
  expiresAt: string | null
}

interface ListApiKeysResponse {
  keys: ApiKeyListItem[]
}

export const apiKeysApi = {
  async listKeys(): Promise<ApiKeyListItem[]> {
    const { data } = await http.get<ListApiKeysResponse>('/apikeys')
    return data.keys
  },

  async createKey(name: string, expiresInDays?: number): Promise<CreateApiKeyResponse> {
    const { data } = await http.post<CreateApiKeyResponse>('/apikeys', {
      name,
      expiresInDays: expiresInDays ?? null,
    })
    return data
  },

  async revokeKey(keyId: string): Promise<void> {
    await http.delete(`/apikeys/${encodeURIComponent(keyId)}`)
  },
}
