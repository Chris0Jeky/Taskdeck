import net from 'node:net'
import { mkdtemp, mkdir, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import path from 'node:path'

import { afterEach, describe, expect, it, vi } from 'vitest'

import {
  resolveDefaultPort,
  runViteDev,
} from '../scripts/run-vite-dev.mjs'

const fixtureRoots: string[] = []

afterEach(async () => {
  await Promise.all(
    fixtureRoots.splice(0).map((fixtureRoot) =>
      rm(fixtureRoot, { force: true, recursive: true }),
    ),
  )
})

describe('Taskdeck Vite development readiness', () => {
  it('emits the exact Vite-resolved fallback URL and preserves serve options', async () => {
    const logs: string[] = []
    const inlineConfigs: unknown[] = []
    const server = createFakeViteServer('http://127.0.0.1:4173/taskdeck/')
    const env: Record<string, string> = {}

    const runningServer = await runViteDev({
      args: [
        'fixture-root',
        '--config=fixture.vite.mjs',
        '--base',
        '/taskdeck/',
        '--mode',
        'fixture',
        '--host',
        '127.0.0.1',
        '--open',
        '/workspace/home',
        '--cors',
        '--force',
        '--strictPort=false',
        '--clearScreen=false',
        '--configLoader',
        'runner',
        '--experimentalBundle',
      ],
      env,
      loadVite: async () => ({
        createServer: async (inlineConfig: unknown) => {
          inlineConfigs.push(inlineConfig)
          return server
        },
      }),
      resolveDefaultPortImpl: async () => 4173,
      transformEntryGraph: async () => ({ entryUrl: '/src/main.ts', moduleCount: 3 }),
      logger: captureLogger(logs),
    })

    expect(runningServer).toBe(server)
    expect(inlineConfigs).toEqual([
      {
        root: 'fixture-root',
        base: '/taskdeck/',
        mode: 'fixture',
        configFile: 'fixture.vite.mjs',
        configLoader: 'runner',
        clearScreen: false,
        forceOptimizeDeps: true,
        experimental: { bundledDev: true },
        server: {
          host: '127.0.0.1',
          port: 4173,
          strictPort: false,
          open: '/workspace/home',
          cors: true,
        },
      },
    ])
    expect(server.listen).toHaveBeenCalledOnce()
    expect(server.printUrls).toHaveBeenCalledOnce()
    expect(server.bindCLIShortcuts).toHaveBeenCalledWith({ print: true })

    const markerLine = logs.find((line) => line.startsWith('TASKDECK_DEV_FRONTEND_READY '))
    expect(markerLine).toBeDefined()
    expect(JSON.parse(markerLine!.slice('TASKDECK_DEV_FRONTEND_READY '.length))).toEqual({
      schemaVersion: 1,
      url: 'http://127.0.0.1:4173/taskdeck/',
      port: 4173,
    })
  })

  it('selects the next bindable fallback after an occupied default port', async () => {
    const bindProbes: number[] = []

    const selectedPort = await resolveDefaultPort('localhost', {
      candidatePorts: [5173, 4173, 5001],
      servesFrontend: async () => false,
      canBind: async (_host, port) => {
        bindProbes.push(port)
        return port === 4173
      },
      logger: captureLogger([]),
    })

    expect(selectedPort).toBe(4173)
    expect(bindProbes).toEqual([5173, 4173])
  })

  it('closes Vite and withholds the marker when a nested direct import is missing', async () => {
    const fixtureRoot = await createFixture({
      'src/main.ts': "import './declared-direct-dependency.ts'\n",
      'src/declared-direct-dependency.ts':
        "import 'taskdeck-missing-declared-direct-dependency'\n",
    })
    const port = await findAvailablePort()
    const logs: string[] = []

    await expect(
      runViteDev({
        args: [fixtureRoot, '--host', '127.0.0.1', '--port', String(port)],
        env: {},
        logger: captureLogger(logs),
      }),
    ).rejects.toThrow(/taskdeck-missing-declared-direct-dependency/)

    expect(logs.some((line) => line.startsWith('TASKDECK_DEV_FRONTEND_READY '))).toBe(false)
    expect(await canBindPort(port)).toBe(true)
    await expect(pathExists(path.join(fixtureRoot, 'dist'))).resolves.toBe(false)
  }, 20_000)

  it('loads the root Vite config and retains proxy settings in a healthy graph', async () => {
    const fixtureRoot = await createFixture({
      'src/main.ts': "import './nested.ts'\n",
      'src/nested.ts': 'export const ready = true\n',
      'vite.config.mjs':
        "export default { server: { proxy: { '/api': 'http://127.0.0.1:5000' } } }\n",
    })
    const port = await findAvailablePort()
    const logs: string[] = []
    const server = await runViteDev({
      args: [fixtureRoot, '--host', '127.0.0.1', '--port', String(port)],
      env: {},
      logger: captureLogger(logs),
    })

    try {
      expect(server.config.server.proxy).toEqual({ '/api': 'http://127.0.0.1:5000' })
      expect(server.config.server.strictPort).toBe(true)
      const markerLine = logs.find((line) => line.startsWith('TASKDECK_DEV_FRONTEND_READY '))
      expect(markerLine).toBeDefined()
      const payload = JSON.parse(markerLine!.slice('TASKDECK_DEV_FRONTEND_READY '.length))
      expect(payload).toEqual({
        schemaVersion: 1,
        url: server.resolvedUrls!.local[0],
        port,
      })
      await expect(pathExists(path.join(fixtureRoot, 'dist'))).resolves.toBe(false)
    } finally {
      await server.close()
    }
  }, 20_000)
})

function createFakeViteServer(resolvedUrl: string) {
  return {
    httpServer: {},
    resolvedUrls: { local: [resolvedUrl], network: [] },
    listen: vi.fn(async () => undefined),
    close: vi.fn(async () => undefined),
    printUrls: vi.fn(),
    bindCLIShortcuts: vi.fn(),
  }
}

function captureLogger(logs: string[]) {
  return {
    log(message: string) {
      logs.push(message)
    },
    warn(message: string) {
      logs.push(message)
    },
  }
}

async function createFixture(files: Record<string, string>) {
  const fixtureRoot = await mkdtemp(path.join(tmpdir(), 'taskdeck-vite-readiness-'))
  fixtureRoots.push(fixtureRoot)

  for (const [relativePath, contents] of Object.entries(files)) {
    const targetPath = path.join(fixtureRoot, relativePath)
    await mkdir(path.dirname(targetPath), { recursive: true })
    await writeFile(targetPath, contents, 'utf8')
  }

  return fixtureRoot
}

async function findAvailablePort() {
  const server = net.createServer()
  await new Promise<void>((resolve, reject) => {
    server.once('error', reject)
    server.listen(0, '127.0.0.1', resolve)
  })
  const address = server.address()
  if (!address || typeof address === 'string') {
    await closeServer(server)
    throw new Error('Could not reserve a fixture port.')
  }

  const { port } = address
  await closeServer(server)
  return port
}

async function canBindPort(port: number) {
  const server = net.createServer()
  try {
    await new Promise<void>((resolve, reject) => {
      server.once('error', reject)
      server.listen(port, '127.0.0.1', resolve)
    })
    return true
  } catch {
    return false
  } finally {
    await closeServer(server)
  }
}

function closeServer(server: net.Server) {
  return new Promise<void>((resolve) => {
    if (!server.listening) {
      resolve()
      return
    }
    server.close(() => resolve())
  })
}

async function pathExists(targetPath: string) {
  try {
    await import('node:fs/promises').then(({ access }) => access(targetPath))
    return true
  } catch {
    return false
  }
}
