const defaultEntryUrl = '/src/main.ts'
const defaultMaxModules = 2_048

/**
 * Ask Vite to resolve and transform every literal import reachable from the
 * Taskdeck entry module. This exercises the development resolver/plugin graph
 * without a browser and without writing a production bundle.
 */
export async function transformDevEntryGraph(
  server,
  { entryUrl = defaultEntryUrl, maxModules = defaultMaxModules } = {},
) {
  if (!Number.isSafeInteger(maxModules) || maxModules < 1) {
    throw new Error('[dev] entry graph module limit must be a positive integer.')
  }

  const environment = server?.environments?.client
  if (!environment?.transformRequest || !environment?.moduleGraph?.getModuleByUrl) {
    throw new Error('[dev] Vite client transform environment is unavailable.')
  }

  const pendingUrls = [entryUrl]
  const transformedUrls = new Set()

  while (pendingUrls.length > 0) {
    const moduleUrl = pendingUrls.shift()
    if (transformedUrls.has(moduleUrl)) {
      continue
    }

    if (transformedUrls.size >= maxModules) {
      throw new Error(
        `[dev] Taskdeck entry graph exceeded the ${maxModules}-module readiness limit.`,
      )
    }

    transformedUrls.add(moduleUrl)

    try {
      const result = await environment.transformRequest(moduleUrl)
      if (!result) {
        throw new Error('Vite returned no transform result.')
      }

      const moduleNode = await environment.moduleGraph.getModuleByUrl(moduleUrl)
      if (!moduleNode) {
        throw new Error('Vite did not register the transformed module in its graph.')
      }

      // Vite keeps literal import URLs separate from plugin-added watch files.
      // Traversing importedModules directly would incorrectly execute Tailwind
      // content dependencies (including Markdown and test fixtures) as modules.
      const importedUrls = moduleNode.staticImportedUrls
      if (importedUrls === undefined) {
        continue
      }

      if (!(importedUrls instanceof Set)) {
        throw new Error('Vite returned an unsupported literal-import graph shape.')
      }

      for (const importedUrl of importedUrls) {
        if (typeof importedUrl !== 'string' || importedUrl.length === 0) {
          throw new Error('Vite returned an invalid literal-import URL.')
        }

        pendingUrls.push(importedUrl)
      }
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error)
      throw new Error(
        `[dev] Taskdeck entry graph transform failed at ${JSON.stringify(moduleUrl)}: ${message}`,
        { cause: error },
      )
    }
  }

  return {
    entryUrl,
    moduleCount: transformedUrls.size,
  }
}
