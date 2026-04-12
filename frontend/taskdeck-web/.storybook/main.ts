import type { StorybookConfig } from '@storybook/vue3-vite'

function isPwaPlugin(plugin: unknown): boolean {
  if (plugin && typeof plugin === 'object' && 'name' in plugin) {
    const name = String((plugin as { name?: unknown }).name ?? '').toLowerCase()
    return name.includes('pwa') || name.includes('workbox')
  }
  return false
}

function stripPwaPlugins(plugins: unknown[]): unknown[] {
  const filtered: unknown[] = []

  for (const plugin of plugins) {
    if (Array.isArray(plugin)) {
      // Recursively filter nested arrays but preserve the array structure
      const filteredNested = stripPwaPlugins(plugin)
      if (filteredNested.length > 0) {
        filtered.push(filteredNested)
      }
      continue
    }
    if (!isPwaPlugin(plugin)) {
      filtered.push(plugin)
    }
  }

  return filtered
}

const config: StorybookConfig = {
  stories: ['../src/**/*.stories.@(ts|tsx)'],
  framework: {
    name: '@storybook/vue3-vite',
    options: {
      docgen: 'vue-component-meta',
    },
  },
  async viteFinal(viteConfig) {
    // Strip PWA plugin — it is app-specific and breaks the Storybook build
    // because the Storybook output includes large JS bundles that exceed
    // the workbox precache size limit.
    return {
      ...viteConfig,
      plugins: viteConfig.plugins
        ? (stripPwaPlugins(viteConfig.plugins as unknown[]) as typeof viteConfig.plugins)
        : viteConfig.plugins,
    }
  },
}

export default config
