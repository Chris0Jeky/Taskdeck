import type { AxiosAdapter } from 'axios'

let adapterPromise: Promise<AxiosAdapter> | undefined

function loadDemoHttpAdapter(): Promise<AxiosAdapter> {
  adapterPromise ??= import('./demoAdapter').then(({ demoHttpAdapter }) => demoHttpAdapter)
  return adapterPromise
}

/**
 * Keeps the large static-demo fixture graph out of the application's eager
 * module graph. The real adapter and its mutable fixtures initialize only when
 * a build that selected demo mode sends its first request. The promise is
 * memoized so concurrent and subsequent requests share one module load.
 */
export const lazyDemoHttpAdapter: AxiosAdapter = async (config) => {
  const adapter = await loadDemoHttpAdapter()
  return adapter(config)
}
