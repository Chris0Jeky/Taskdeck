/**
 * Mock for the 'virtual:pwa-register' module provided by vite-plugin-pwa.
 * The real module is a Vite virtual module that only exists at build time.
 * This mock provides a no-op registerSW for unit tests.
 */
export function registerSW(_options?: {
  onNeedRefresh?: () => void
  onOfflineReady?: () => void
  onRegistered?: (registration: ServiceWorkerRegistration | undefined) => void
  onRegisteredSW?: (swScriptUrl: string, registration: ServiceWorkerRegistration | undefined) => void
  onRegisterError?: (error: unknown) => void
}): (reloadPage?: boolean) => Promise<void> {
  return async () => {}
}
