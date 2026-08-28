import http from './http'

export type ApiKeyScopeName = 'read' | 'propose' | 'manage'

export interface ApiKeyListItem {
  id: string
  keyPrefix: string
  name: string
  scopes: ApiKeyScopeName[]
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
  scopes: ApiKeyScopeName[]
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

  async createKey(
    name: string,
    scopes: readonly ApiKeyScopeName[],
    expiresInDays?: number,
  ): Promise<CreateApiKeyResponse> {
    const knownScopes: readonly ApiKeyScopeName[] = ['read', 'propose', 'manage']
    if (scopes.length === 0 || scopes.some((scope) => !knownScopes.includes(scope))) {
      throw new Error('Select at least one known API key scope.')
    }

    const { data } = await http.post<CreateApiKeyResponse>('/apikeys', {
      name,
      scopes: [...scopes],
      expiresInDays: expiresInDays ?? null,
    })
    return data
  },

  async revokeKey(keyId: string): Promise<void> {
    await http.delete(`/apikeys/${encodeURIComponent(keyId)}`)
  },
}
