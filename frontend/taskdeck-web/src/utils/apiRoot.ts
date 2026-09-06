/**
 * Remove exactly one terminal `/api` segment while preserving deployment
 * subpaths, origins, and unrelated path segments.
 */
export function apiRootFrom(apiBase: string): string {
  return apiBase.replace(/\/api\/?$/i, '')
}
