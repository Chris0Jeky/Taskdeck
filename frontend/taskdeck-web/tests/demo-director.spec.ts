import fs from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { afterEach, describe, expect, it } from 'vitest'

import {
  resetDemoDirectorArtifacts,
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
    expect(runtime.e2eDbPath).toBe(path.join(webRoot, 'taskdeck.demo.ci.db'))

    await fs.writeFile(runtime.e2eDbPath!, 'sqlite bytes', 'utf8')
    await fs.writeFile(path.join(webRoot, 'keep.txt'), 'preserve me', 'utf8')

    await resetDemoDirectorE2EDb(runtime.e2eDbPath)

    expect(await pathExists(runtime.e2eDbPath!)).toBe(false)
    await expect(fs.readFile(path.join(webRoot, 'keep.txt'), 'utf8')).resolves.toBe('preserve me')
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
})
