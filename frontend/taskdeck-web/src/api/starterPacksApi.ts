import http from './http'
import type {
  ApplyStarterPackDto,
  StarterPackApplyResult,
  StarterPackCatalogEntry,
  ValidateManifestResult,
} from '../types/starter-packs'

export const starterPacksApi = {
  async getCatalog(boardId: string): Promise<StarterPackCatalogEntry[]> {
    const { data } = await http.get<StarterPackCatalogEntry[]>(`/boards/${boardId}/starter-packs/catalog`)
    return data
  },

  async applyStarterPack(boardId: string, request: ApplyStarterPackDto): Promise<StarterPackApplyResult> {
    const { data } = await http.post<StarterPackApplyResult>(`/boards/${boardId}/starter-packs/apply`, request)
    return data
  },

  async validateManifestJson(boardId: string, manifestJson: string): Promise<ValidateManifestResult> {
    const { data } = await http.post<ValidateManifestResult>(
      `/boards/${boardId}/starter-packs/validate-manifest`,
      { manifestJson },
    )
    return data
  },
}
