import fs from 'node:fs/promises'
import net from 'node:net'
import path from 'node:path'

const DIRECTOR_ARTIFACT_ENTRIES = [
  'README.md',
  'run-summary.json',
  'snapshot.json',
  'trace.ndjson',
  'logs',
  'screenshots',
  'playwright',
]
const DEFAULT_DEMO_DIRECTOR_API_BASE_URL = 'http://localhost:5000/api'

export async function resetDemoDirectorArtifacts(artifactDir) {
  const targets = DIRECTOR_ARTIFACT_ENTRIES.map((entry) => path.join(artifactDir, entry))
  await Promise.all(targets.map((targetPath) => fs.rm(targetPath, { recursive: true, force: true })))
}

export function resolveDemoDirectorRuntime({ webRoot, e2eDb, resetE2EDb = false, freshServers = false }) {
  const normalizedE2EDb = typeof e2eDb === 'string' ? e2eDb.trim() : ''
  if (resetE2EDb && normalizedE2EDb.length === 0) {
    throw new Error('--reset-e2e-db requires --e2e-db')
  }

  return {
    e2eDbPath: normalizedE2EDb.length > 0 ? path.resolve(webRoot, normalizedE2EDb) : null,
    forceFreshServers: freshServers || resetE2EDb,
  }
}

export async function resetDemoDirectorE2EDb(e2eDbPath) {
  if (!e2eDbPath) {
    return
  }

  await fs.rm(e2eDbPath, { force: true })
}

export async function resolveDemoDirectorApiBaseUrl({
  requestedApiBaseUrl,
  defaultApiBaseUrl = DEFAULT_DEMO_DIRECTOR_API_BASE_URL,
  forceFreshServers = false,
  canBind = canBindTcpPort,
  reservePort = reserveTcpPort,
} = {}) {
  const normalizedRequestedApiBaseUrl = normalizeUrlString(requestedApiBaseUrl)
  if (normalizedRequestedApiBaseUrl) {
    return normalizedRequestedApiBaseUrl
  }

  const normalizedDefaultApiBaseUrl = normalizeUrlString(defaultApiBaseUrl)
  if (!forceFreshServers) {
    return normalizedDefaultApiBaseUrl
  }

  const parsedDefaultApiBaseUrl = new URL(normalizedDefaultApiBaseUrl)
  const defaultPort = getUrlPort(parsedDefaultApiBaseUrl)
  if (await canBind(parsedDefaultApiBaseUrl.hostname, defaultPort)) {
    return normalizedDefaultApiBaseUrl
  }

  const fallbackPort = await reservePort(parsedDefaultApiBaseUrl.hostname)
  parsedDefaultApiBaseUrl.port = String(fallbackPort)
  return normalizeUrlString(parsedDefaultApiBaseUrl.toString())
}

export function buildDemoDirectorPortConflictHint(playwrightLog) {
  const normalizedLog = typeof playwrightLog === 'string' ? playwrightLog : ''
  if (!/(EADDRINUSE|address already in use|port is already used|listen EACCES)/i.test(normalizedLog)) {
    return null
  }

  return (
    'Fresh-server startup hit a local port conflict. ' +
    'Rerun with free local overrides such as TASKDECK_E2E_API_BASE_URL=http://localhost:<port>/api ' +
    'and TASKDECK_E2E_FRONTEND_PORT=<port>, or stop the existing Taskdeck listeners first.'
  )
}

export { DIRECTOR_ARTIFACT_ENTRIES }

function normalizeUrlString(value) {
  if (typeof value !== 'string') {
    return null
  }

  const trimmed = value.trim()
  if (trimmed.length === 0) {
    return null
  }

  return trimmed.endsWith('/') ? trimmed.slice(0, -1) : trimmed
}

function getUrlPort(url) {
  if (url.port) {
    return Number(url.port)
  }

  if (url.protocol === 'http:') {
    return 80
  }

  if (url.protocol === 'https:') {
    return 443
  }

  throw new Error(`Unsupported protocol for demo director API base URL: ${url.protocol}`)
}

async function canBindTcpPort(host, port) {
  return await new Promise((resolve) => {
    const server = net.createServer()

    server.once('error', () => {
      resolve(false)
    })

    server.listen(port, host, () => {
      server.close(() => resolve(true))
    })
  })
}

async function reserveTcpPort(host) {
  return await new Promise((resolve, reject) => {
    const server = net.createServer()

    server.once('error', (error) => {
      reject(error)
    })

    server.listen(0, host, () => {
      const address = server.address()
      const reservedPort = typeof address === 'object' && address ? address.port : null
      server.close((closeError) => {
        if (closeError) {
          reject(closeError)
          return
        }

        if (!reservedPort) {
          reject(new Error(`Unable to reserve a fallback port for host "${host}".`))
          return
        }

        resolve(reservedPort)
      })
    })
  })
}
