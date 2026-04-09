import type { StorybookConfig } from '@storybook/vue3-vite'

function isPwaPlugin(plugin: unknown): boolean {
  if (plugin && typeof plugin === 'object' && 'name' in plugin) {
    const name = (plugin as { name: string }).name
    return name.includes('pwa') || name.includes('workbox') || name.includes('PWA')
  }
  return false
}

function stripPwaPlugins(plugins: unknown[]): unknown[] {
  return plugins.filter((plugin) => {
    // Some Vite plugins (including VitePWA) return arrays of sub-plugins.
    // Check each element to decide whether the whole array should be dropped.
    if (Array.isArray(plugin)) {
      return !plugin.some(isPwaPlugin)
    }
    return !isPwaPlugin(plugin)
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
