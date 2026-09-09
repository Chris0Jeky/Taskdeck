import { createHash } from 'node:crypto'
import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import type { Plugin, ResolvedConfig } from 'vite'

/** Input fingerprint, not a release label or a claim that an external server has these bytes. */
export function fingerprintFrontend(root: string, mode: string, publicEnvironment: Record<string, string>, buildOptions: Record<string, unknown> = {}): string {
  const digest = createHash('sha256')
  const add = (name: string, bytes: Uint8Array) => {
    digest.update(`${name.length}:${name}:${bytes.length}:`)
    digest.update(bytes)
  }
  function directory(relative: string) {
    for (const entry of readdirSync(join(root, relative), { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name, 'en'))) {
      const name = `${relative}/${entry.name}`
      if (entry.isDirectory()) directory(name)
      else if (entry.isFile()) add(name, readFileSync(join(root, name)))
      else throw new Error(`Unsupported frontend input: ${name}`)
    }
  }
  for (const folder of ['src', 'public', 'build']) directory(folder)
  for (const file of ['index.html', 'package.json', 'package-lock.json', 'vite.config.ts', 'tsconfig.app.json', 'tsconfig.node.json', 'tsconfig.json', 'postcss.config.js', 'tailwind.config.js'])
    add(file, readFileSync(join(root, file)))
  add('build-environment', Buffer.from(JSON.stringify({ mode, node: process.versions.node, buildOptions,
    values: Object.fromEntries(Object.entries(publicEnvironment).sort(([a], [b]) => a.localeCompare(b, 'en'))) })))
  return `sha256:${digest.digest('hex')}`
}

export function frontendIdentityPlugin(): Plugin {
  let config: ResolvedConfig
  let identity: string
  return {
    name: 'taskdeck:frontend-input-identity', apply: 'build', enforce: 'pre',
    configResolved(value) { config = value },
    buildStart() {
      const build = config.build
      identity = fingerprintFrontend(config.root, config.mode, config.env, {
        base: config.base, production: config.isProduction, target: build.target, minify: build.minify,
        cssTarget: build.cssTarget, cssMinify: build.cssMinify, cssCodeSplit: build.cssCodeSplit,
        assetsDir: build.assetsDir, assetsInlineLimit: String(build.assetsInlineLimit), sourcemap: build.sourcemap,
        modulePreload: build.modulePreload, ssr: build.ssr, lib: build.lib, define: config.define,
      })
    },
    transform(_code, id) {
      if (id.replaceAll('\\', '/').split('?')[0]?.endsWith('/src/utils/frontendBuildIdentity.ts'))
        return { code: `export const frontendBuildIdentity = ${JSON.stringify(identity)};`, map: null }
    },
  }
}
