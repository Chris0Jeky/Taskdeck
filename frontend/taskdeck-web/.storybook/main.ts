import type { StorybookConfig } from '@storybook/vue3-vite'

function stripPwaPlugins(plugins: unknown[]): unknown[] {
  return plugins.filter((plugin) => {
    // Handle arrays (some Vite plugins return arrays of sub-plugins)
    if (Array.isArray(plugin)) {
      return false // PWA returns an array; but be safe, check names
    }
    if (plugin && typeof plugin === 'object' && 'name' in plugin) {
      const name = (plugin as { name: string }).name
      if (name.includes('pwa') || name.includes('workbox') || name.includes('PWA')) {
        return false
      }
    }
    return true
  })
}

const config: StorybookConfig = {
  stories: ['../src/**/*.stories.@(ts|tsx)'],
  framework: {
    name: '@storybook/vue3-vite',
    options: {
      docgen: 'vue-component-meta',
    },
  },
  async viteFinal(config) {
    // Strip PWA plugin — it is app-specific and breaks the Storybook build
    // because the Storybook output includes large JS bundles that exceed
    // the workbox precache size limit.
    if (config.plugins) {
      config.plugins = stripPwaPlugins(config.plugins as unknown[])
    }
    return config
  },
}

export default config
