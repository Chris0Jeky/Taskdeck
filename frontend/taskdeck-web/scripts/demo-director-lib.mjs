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
  const resolvedWebRoot = path.resolve(webRoot)
  const normalizedE2EDb = typeof e2eDb === 'string' ? e2eDb.trim() : ''
  if (resetE2EDb && normalizedE2EDb.length === 0) {
    throw new Error('--reset-e2e-db requires --e2e-db')
  }

  let e2eDbPath = null
  if (normalizedE2EDb.length > 0) {
    if (resetE2EDb && path.isAbsolute(normalizedE2EDb)) {
      throw new Error('--e2e-db must be a path within the web root when --reset-e2e-db is used')
    }

    const candidateE2EDbPath = path.resolve(resolvedWebRoot, normalizedE2EDb)
    if (resetE2EDb) {
      const relativeToRoot = path.relative(resolvedWebRoot, candidateE2EDbPath)
      if (relativeToRoot.startsWith('..') || path.isAbsolute(relativeToRoot)) {
        throw new Error('--e2e-db must not point outside the web root when --reset-e2e-db is used')
      }
    }

    e2eDbPath = candidateE2EDbPath
  }

  return {
    e2eDbPath,
    shouldResetE2EDb: resetE2EDb,
    forceFreshServers: freshServers || resetE2EDb,
  }
}

export async function resetDemoDirectorE2EDb(e2eDbPath) {
  if (!e2eDbPath) {
    return
  }

  await Promise.all(getDemoDirectorE2EDbTargets(e2eDbPath).map((targetPath) => fs.rm(targetPath, { force: true })))
}

export async function resolveDemoDirectorApiBaseUrl({
  requestedApiBaseUrl,
  defaultApiBaseUrl = DEFAULT_DEMO_DIRECTOR_API_BASE_URL,
  forceFreshServers = false,
  canBind = canBindTcpPort,
  reservePort = reserveTcpPort,
  maxPortProbeAttempts = 10,
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
  const hostsToProbe = resolveLoopbackHosts(parsedDefaultApiBaseUrl.hostname)
  if (await canBindAllTcpHosts(hostsToProbe, defaultPort, canBind)) {
    return normalizedDefaultApiBaseUrl
  }

  const fallbackPort = await reserveBindableTcpPort({
    hosts: hostsToProbe,
    reservePort,
    canBind,
    maxAttempts: maxPortProbeAttempts,
  })
  parsedDefaultApiBaseUrl.port = String(fallbackPort)
  return normalizeUrlString(parsedDefaultApiBaseUrl.toString())
}

export function resolveDemoDirectorRequestedApiBaseUrl({
  e2eApiBaseUrl,
  apiBaseUrl,
  apiBase,
  forceFreshServers = false,
}) {
  const normalizedE2EApiBaseUrl = normalizeUrlString(e2eApiBaseUrl)
  if (normalizedE2EApiBaseUrl) {
    return normalizedE2EApiBaseUrl
  }

  if (forceFreshServers) {
    return null
  }

  return normalizeUrlString(apiBaseUrl) ?? normalizeUrlString(apiBase)
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

export function applyDemoDirectorApiBaseUrl(env, apiBaseUrl) {
  return {
    ...env,
    TASKDECK_API_BASE_URL: apiBaseUrl,
    TASKDECK_API_BASE: apiBaseUrl,
    TASKDECK_E2E_API_BASE_URL: apiBaseUrl,
  }
}

export function createDemoDirectorRunSummary({
  runId,
  startedAt,
  endedAt,
  playwrightExitCode,
  playwrightSignal,
  args,
  selectedBoardName,
  screenshots,
  summary,
  events,
  portConflictHint,
}) {
  const runStatus =
    playwrightExitCode === 0 ? 'ok' : playwrightSignal ? `error (signal ${playwrightSignal})` : 'error'

  return {
    runId,
    startedAt,
    endedAt,
    status: runStatus,
    playwrightExitCode,
    playwrightSignal,
    scenario: args.scenario,
    skipSeed: args.skipSeed,
    skipLlm: args.skipLlm,
    autopilot: {
      enabled: args.turns > 0,
      turns: args.turns,
      board: selectedBoardName,
      loop: args.loop,
      brain: args.brain,
      intervalMs: args.intervalMs,
      rngSeed: args.rngSeed || null,
    },
    artifacts: {
      trace: 'trace.ndjson',
      snapshot: 'snapshot.json',
      logsDir: 'logs/',
      screenshotsDir: 'screenshots/',
      playwrightDir: 'playwright/',
    },
    screenshots,
    stats: {
      events: events.length,
      byType: summary.byType,
      autopilot: summary.autopilot,
      proposals: summary.proposals.length,
      captures: summary.captures.length,
    },
    diagnostics: {
      hints: portConflictHint ? [portConflictHint] : [],
    },
  }
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

    server.once('error', (error) => {
      if (error?.code === 'EAFNOSUPPORT' || error?.code === 'EADDRNOTAVAIL') {
        resolve({ available: false, unsupported: true })
        return
      }

      resolve({ available: false, unsupported: false })
    })

    server.listen(port, host, () => {
      server.close(() => resolve({ available: true, unsupported: false }))
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

async function canBindAllTcpHosts(hosts, port, canBind) {
  for (const host of hosts) {
    const result = normalizeBindProbeResult(await canBind(host, port))
    if (result.unsupported) {
      continue
    }

    if (!result.available) {
      return false
    }
  }

  return true
}

async function reserveBindableTcpPort({ hosts, reservePort, canBind, maxAttempts }) {
  const primaryHost = hosts[0]
  let lastReservedPort = null

  for (let attempt = 0; attempt < maxAttempts; attempt++) {
    const reservedPort = await reservePort(primaryHost)
    lastReservedPort = reservedPort
    if (await canBindAllTcpHosts(hosts, reservedPort, canBind)) {
      return reservedPort
    }
  }

  throw new Error(
    `Unable to reserve a fallback port that is free on all loopback hosts after ${maxAttempts} attempts. ` +
      `Last probed port: ${lastReservedPort ?? 'unknown'}.`,
  )
}

function resolveLoopbackHosts(hostname) {
  const normalizedHost = String(hostname || '').trim().toLowerCase()
  if (normalizedHost === 'localhost') {
    return ['localhost', '127.0.0.1', '::1']
  }

  if (normalizedHost === '127.0.0.1' || normalizedHost === '::1') {
    return [normalizedHost]
  }

  return [hostname]
}

function normalizeBindProbeResult(result) {
  if (typeof result === 'boolean') {
    return { available: result, unsupported: false }
  }

  if (result && typeof result === 'object') {
    return {
      available: Boolean(result.available),
      unsupported: Boolean(result.unsupported),
    }
  }

  return { available: false, unsupported: false }
}

function getDemoDirectorE2EDbTargets(e2eDbPath) {
  return [
    e2eDbPath,
    `${e2eDbPath}-wal`,
    `${e2eDbPath}-shm`,
    `${e2eDbPath}-journal`,
  ]
}
