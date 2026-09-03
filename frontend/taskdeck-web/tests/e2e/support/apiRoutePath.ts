export function apiRoutePath(apiBaseUrl: string, routePath: string): string {
  const basePath = new URL(apiBaseUrl).pathname.replace(/\/+$/, '')
  const normalizedRoutePath = routePath.replace(/^\/+/, '')

  return `${basePath}/${normalizedRoutePath}`
}
