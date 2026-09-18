import net from 'node:net'
import { access, mkdtemp, mkdir, realpath, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import path from 'node:path'

import { afterEach, describe, expect, it } from 'vitest'

import { runViteDev } from '../scripts/run-vite-dev.mjs'

const fixtureRoots: string[] = []

afterEach(async () => {
  await Promise.all(
    fixtureRoots.splice(0).map((fixtureRoot) =>
      rm(fixtureRoot, { force: true, recursive: true }),
    ),
  )
})

describe('Taskdeck Vite lazy-route readiness', () => {
  it('withholds readiness when a literal lazy route has a broken nested import', async () => {
    const fixtureRoot = await createFixture({
      'src/main.ts': "export const loadLazyRoute = () => import('./lazy-route.ts')\n",
      'src/lazy-route.ts': "import 'taskdeck-missing-lazy-route-dependency'\n",
    })
    const port = await findAvailablePort()
    const logs: string[] = []

    await expect(
      runViteDev({
        args: [fixtureRoot, '--host', '127.0.0.1', '--port', String(port)],
        env: {},
        logger: captureLogger(logs),
      }),
    ).rejects.toThrow(/taskdeck-missing-lazy-route-dependency/)

    expect(logs.some((line) => line.startsWith('TASKDECK_DEV_FRONTEND_READY '))).toBe(false)
    expect(await canBindPort(port)).toBe(true)
    await expect(pathExists(path.join(fixtureRoot, 'dist'))).resolves.toBe(false)
  }, 20_000)
})

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
  const fixtureRoot = await realpath(
    await mkdtemp(path.join(tmpdir(), 'taskdeck-vite-lazy-readiness-')),
  )
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
    await access(targetPath)
    return true
  } catch {
    return false
  }
}
