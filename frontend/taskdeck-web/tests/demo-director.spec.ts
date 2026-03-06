import fs from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { afterEach, describe, expect, it } from 'vitest'

import {
  applyDemoDirectorApiBaseUrl,
  buildDemoDirectorPortConflictHint,
  resetDemoDirectorArtifacts,
  resolveDemoDirectorApiBaseUrl,
  resolveDemoDirectorRequestedApiBaseUrl,
  resetDemoDirectorE2EDb,
  resolveDemoDirectorRuntime,
} from '../scripts/demo-director-lib.mjs'

const tempDirs: string[] = []

async function pathExists(targetPath: string) {
  try {
    await fs.access(targetPath)
    return true
  } catch {
    return false
  }
}

describe('demo director artifacts', () => {
  afterEach(async () => {
    await Promise.all(tempDirs.splice(0).map((dirPath) => fs.rm(dirPath, { recursive: true, force: true })))
  })

  it('removes only director-managed artifacts before a rerun', async () => {
    const artifactDir = await fs.mkdtemp(path.join(os.tmpdir(), 'taskdeck-demo-director-'))
    tempDirs.push(artifactDir)

    await fs.mkdir(path.join(artifactDir, 'logs'), { recursive: true })
    await fs.mkdir(path.join(artifactDir, 'screenshots'), { recursive: true })
    await fs.mkdir(path.join(artifactDir, 'playwright'), { recursive: true })
    await fs.writeFile(path.join(artifactDir, 'README.md'), 'stale readme', 'utf8')
    await fs.writeFile(path.join(artifactDir, 'run-summary.json'), '{"status":"stale"}', 'utf8')
    await fs.writeFile(path.join(artifactDir, 'snapshot.json'), '{"snapshot":true}', 'utf8')
    await fs.writeFile(path.join(artifactDir, 'trace.ndjson'), '{"type":"stale"}\n', 'utf8')
    await fs.writeFile(path.join(artifactDir, 'logs', 'playwright.log'), 'stale log', 'utf8')
    await fs.writeFile(path.join(artifactDir, 'screenshots', 'shot.png'), 'png', 'utf8')
    await fs.writeFile(path.join(artifactDir, 'playwright', 'result.json'), '{}', 'utf8')
    await fs.writeFile(path.join(artifactDir, 'keep.txt'), 'preserve me', 'utf8')

    await resetDemoDirectorArtifacts(artifactDir)

    expect(await pathExists(path.join(artifactDir, 'README.md'))).toBe(false)
    expect(await pathExists(path.join(artifactDir, 'run-summary.json'))).toBe(false)
    expect(await pathExists(path.join(artifactDir, 'snapshot.json'))).toBe(false)
    expect(await pathExists(path.join(artifactDir, 'trace.ndjson'))).toBe(false)
    expect(await pathExists(path.join(artifactDir, 'logs'))).toBe(false)
    expect(await pathExists(path.join(artifactDir, 'screenshots'))).toBe(false)
    expect(await pathExists(path.join(artifactDir, 'playwright'))).toBe(false)

    await expect(fs.readFile(path.join(artifactDir, 'keep.txt'), 'utf8')).resolves.toBe('preserve me')
  })

  it('resolves and resets an isolated smoke database without touching sibling files', async () => {
    const webRoot = await fs.mkdtemp(path.join(os.tmpdir(), 'taskdeck-demo-runtime-'))
    tempDirs.push(webRoot)

    const runtime = resolveDemoDirectorRuntime({
      webRoot,
      e2eDb: './taskdeck.demo.ci.db',
      resetE2EDb: true,
      freshServers: false,
    })

    expect(runtime.forceFreshServers).toBe(true)
    expect(runtime.shouldResetE2EDb).toBe(true)
    expect(runtime.e2eDbPath).toBe(path.join(webRoot, 'taskdeck.demo.ci.db'))

    await fs.writeFile(runtime.e2eDbPath!, 'sqlite bytes', 'utf8')
    await fs.writeFile(`${runtime.e2eDbPath!}-wal`, 'wal bytes', 'utf8')
    await fs.writeFile(`${runtime.e2eDbPath!}-shm`, 'shm bytes', 'utf8')
    await fs.writeFile(`${runtime.e2eDbPath!}-journal`, 'journal bytes', 'utf8')
    await fs.writeFile(path.join(webRoot, 'keep.txt'), 'preserve me', 'utf8')

    await resetDemoDirectorE2EDb(runtime.e2eDbPath)

    expect(await pathExists(runtime.e2eDbPath!)).toBe(false)
    expect(await pathExists(`${runtime.e2eDbPath!}-wal`)).toBe(false)
    expect(await pathExists(`${runtime.e2eDbPath!}-shm`)).toBe(false)
    expect(await pathExists(`${runtime.e2eDbPath!}-journal`)).toBe(false)
    await expect(fs.readFile(path.join(webRoot, 'keep.txt'), 'utf8')).resolves.toBe('preserve me')
  })

  it('keeps a configured e2e database path without resetting it when the reset flag is absent', async () => {
    const webRoot = await fs.mkdtemp(path.join(os.tmpdir(), 'taskdeck-demo-runtime-'))
    tempDirs.push(webRoot)

    const runtime = resolveDemoDirectorRuntime({
      webRoot,
      e2eDb: './taskdeck.demo.shared.db',
      resetE2EDb: false,
      freshServers: false,
    })

    expect(runtime.forceFreshServers).toBe(false)
    expect(runtime.shouldResetE2EDb).toBe(false)
    expect(runtime.e2eDbPath).toBe(path.join(webRoot, 'taskdeck.demo.shared.db'))
  })

  it('rejects resetting the e2e database when no path is configured', () => {
    expect(() =>
      resolveDemoDirectorRuntime({
        webRoot: os.tmpdir(),
        e2eDb: null,
        resetE2EDb: true,
        freshServers: false,
      }),
    ).toThrow('--reset-e2e-db requires --e2e-db')
  })

  it('rejects resetting an absolute e2e database path', () => {
    expect(() =>
      resolveDemoDirectorRuntime({
        webRoot: os.tmpdir(),
        e2eDb: path.join(os.tmpdir(), 'taskdeck.demo.ci.db'),
        resetE2EDb: true,
        freshServers: false,
      }),
    ).toThrow('--e2e-db must be a path within the web root when --reset-e2e-db is used')
  })

  it('rejects resetting an e2e database path outside the web root', async () => {
    const webRoot = await fs.mkdtemp(path.join(os.tmpdir(), 'taskdeck-demo-runtime-'))
    tempDirs.push(webRoot)

    expect(() =>
      resolveDemoDirectorRuntime({
        webRoot,
        e2eDb: '../taskdeck.demo.ci.db',
        resetE2EDb: true,
        freshServers: false,
      }),
    ).toThrow('--e2e-db must not point outside the web root when --reset-e2e-db is used')
  })

  it('falls back to a free api port when fresh-server mode cannot bind the default backend port', async () => {
    const resolvedApiBaseUrl = await resolveDemoDirectorApiBaseUrl({
      requestedApiBaseUrl: null,
      forceFreshServers: true,
      canBind: async (_host, port) => port !== 5000,
      reservePort: async () => 51234,
    })

    expect(resolvedApiBaseUrl).toBe('http://localhost:51234/api')
  })

  it('probes both localhost loopback families before keeping the default api port', async () => {
    const bindChecks: Array<{ host: string; port: number }> = []

    const resolvedApiBaseUrl = await resolveDemoDirectorApiBaseUrl({
      requestedApiBaseUrl: null,
      forceFreshServers: true,
      canBind: async (host, port) => {
        bindChecks.push({ host, port })
        return !(host === '127.0.0.1' && port === 5000)
      },
      reservePort: async () => 51235,
    })

    expect(resolvedApiBaseUrl).toBe('http://localhost:51235/api')
    expect(bindChecks).toEqual([
      { host: 'localhost', port: 5000 },
      { host: '127.0.0.1', port: 5000 },
      { host: 'localhost', port: 51235 },
      { host: '127.0.0.1', port: 51235 },
      { host: '::1', port: 51235 },
    ])
  })

  it('ignores unsupported loopback families while still checking supported ones', async () => {
    const resolvedApiBaseUrl = await resolveDemoDirectorApiBaseUrl({
      requestedApiBaseUrl: null,
      forceFreshServers: true,
      canBind: async (host) => {
        if (host === '::1') {
          return { available: false, unsupported: true }
        }

        return true
      },
      reservePort: async () => 51236,
    })

    expect(resolvedApiBaseUrl).toBe('http://localhost:5000/api')
  })

  it('ignores inherited generic demo api overrides when fresh-server mode is enabled', () => {
    const requestedApiBaseUrl = resolveDemoDirectorRequestedApiBaseUrl({
      e2eApiBaseUrl: null,
      apiBaseUrl: 'http://localhost:5000/api',
      apiBase: 'http://localhost:5000/api',
      forceFreshServers: true,
    })

    expect(requestedApiBaseUrl).toBeNull()
  })

  it('keeps dedicated e2e api overrides in fresh-server mode', () => {
    const requestedApiBaseUrl = resolveDemoDirectorRequestedApiBaseUrl({
      e2eApiBaseUrl: 'http://localhost:5001/api',
      apiBaseUrl: 'http://localhost:5000/api',
      apiBase: 'http://localhost:5000/api',
      forceFreshServers: true,
    })

    expect(requestedApiBaseUrl).toBe('http://localhost:5001/api')
  })

  it('keeps demo helper api env variables aligned to the selected api base url', () => {
    const env = applyDemoDirectorApiBaseUrl(
      {
        TASKDECK_API_BASE_URL: 'http://localhost:5000/api',
        TASKDECK_API_BASE: 'http://localhost:5000/api',
        TASKDECK_E2E_API_BASE_URL: 'http://localhost:5000/api',
      },
      'http://localhost:51234/api',
    )

    expect(env.TASKDECK_API_BASE_URL).toBe('http://localhost:51234/api')
    expect(env.TASKDECK_API_BASE).toBe('http://localhost:51234/api')
    expect(env.TASKDECK_E2E_API_BASE_URL).toBe('http://localhost:51234/api')
  })

  it('reports an actionable hint for fresh-server port conflicts', () => {
    const hint = buildDemoDirectorPortConflictHint('listen EADDRINUSE: address already in use 127.0.0.1:5000')

    expect(hint).toContain('TASKDECK_E2E_API_BASE_URL')
    expect(hint).toContain('TASKDECK_E2E_FRONTEND_PORT')
  })
})
