import { spawnSync } from 'node:child_process'

export const defaultFrontendHost = 'localhost'
export const defaultFrontendPort = 5173
export const fallbackFrontendPorts = [defaultFrontendPort, 4173, 5001] as const
const taskdeckFrontendIdentityMarkers = ['<title>taskdeck-web</title>', '/src/main.ts'] as const
export const portProbeTimeoutMs = 300

type PortProbe = (host: string, port: number) => boolean
type ProbeResult = {
  error?: Error
  status: number | null
}
type ProbeRunner = (candidateHost: string, port: number, probeScript: string) => ProbeResult
type ProbeOptions = {
  onProbeError?: (message: string) => void
  probeRunner?: ProbeRunner
}

type ResolveDefaultFrontendPortOptions = {
  bindProbe?: PortProbe
  connectProbe?: PortProbe
  fallbackPorts?: readonly number[]
  onFallback?: (message: string) => void
}

export function resolveDefaultFrontendPort(
  host: string,
  options?: ResolveDefaultFrontendPortOptions,
): number {
  const normalizedHost = parseFrontendHost(host, 'TASKDECK_E2E_FRONTEND_HOST')
  const connectProbe = options?.connectProbe ?? canConnectToTaskdeckFrontend
  const bindProbe = options?.bindProbe ?? canBindPort
  const candidatePorts = options?.fallbackPorts ?? fallbackFrontendPorts

  for (const candidatePort of candidatePorts) {
    if (connectProbe(normalizedHost, candidatePort)) {
      return candidatePort
    }
  }

  for (const candidatePort of candidatePorts) {
    if (bindProbe(normalizedHost, candidatePort)) {
      return candidatePort
    }
  }

  const fallbackMessage =
    `[e2e config] Could not find a running Taskdeck frontend or bindable frontend port in ` +
    `${candidatePorts.join(', ')} for host "${normalizedHost}". Falling back to ${defaultFrontendPort}. ` +
    `If startup still fails, set TASKDECK_E2E_FRONTEND_PORT to an explicit free port.`
  ;(options?.onFallback ?? console.warn)(fallbackMessage)
  return defaultFrontendPort
}

export function canConnectToTaskdeckFrontend(
  host: string,
  port: number,
  options?: ProbeOptions,
): boolean {
  const probeHost = parseFrontendHost(host, 'TASKDECK_E2E_FRONTEND_HOST')
  const identityMarkersLiteral = JSON.stringify(taskdeckFrontendIdentityMarkers)
  const probeRunner = options?.probeRunner ?? runProbeScript
  const onProbeError = options?.onProbeError ?? console.warn
  const probeScript = `
const http = require('node:http')
const host = process.argv[1]
const port = Number(process.argv[2])
const markers = ${identityMarkersLiteral}
const probeTimeoutMs = Number(process.argv[3])

let settled = false
const settle = (statusCode) => {
  if (settled) {
    return
  }

  settled = true
  clearTimeout(timeoutHandle)
  process.exit(statusCode)
}

const request = http.request(
  {
    host,
    port,
    method: 'GET',
    path: '/',
    headers: { accept: 'text/html' },
  },
  (response) => {
    let responseText = ''
    response.setEncoding('utf8')
    response.on('data', (chunk) => {
      if (responseText.length < 8192) {
        responseText += chunk
      }
    })
    response.on('end', () => {
      const statusCode = response.statusCode ?? 0
      const hasExpectedIdentity = markers.every((marker) => responseText.includes(marker))
      settle(statusCode === 200 && hasExpectedIdentity ? 0 : 1)
    })
  },
)

const timeoutHandle = setTimeout(() => {
  request.destroy()
  settle(1)
}, probeTimeoutMs)

request.on('error', () => settle(1))
request.end()
`.trim()

  for (const candidateHost of resolvePortProbeHosts(probeHost)) {
    const probe = probeRunner(candidateHost, port, probeScript)

    if (probe.error) {
      onProbeError(
        `[e2e config] frontend identity probe spawn failed for ${candidateHost}:${port}: ${probe.error.message}`,
      )
      continue
    }

    if (probe.status === 0) {
      return true
    }
  }

  return false
}

export function canBindPort(host: string, port: number, options?: ProbeOptions): boolean {
  const probeHost = parseFrontendHost(host, 'TASKDECK_E2E_FRONTEND_HOST')
  const probeRunner = options?.probeRunner ?? runProbeScript
  const onProbeError = options?.onProbeError ?? console.warn
  const probeScript = `
const net = require('node:net')
const host = process.argv[1]
const port = Number(process.argv[2])
const probeTimeoutMs = Number(process.argv[3])
const server = net.createServer()

let settled = false
const settle = (statusCode) => {
  if (settled) {
    return
  }

  settled = true
  clearTimeout(timeoutHandle)
  try {
    server.close()
  } catch {
    // no-op
  }
  process.exit(statusCode)
}

const timeoutHandle = setTimeout(() => settle(1), probeTimeoutMs)

server.once('error', () => settle(1))
server.listen(port, host, () => {
  server.close(() => settle(0))
})
`.trim()

  for (const candidateHost of resolvePortProbeHosts(probeHost)) {
    const probe = probeRunner(candidateHost, port, probeScript)

    if (probe.error) {
      onProbeError(
        `[e2e config] frontend bind probe spawn failed for ${candidateHost}:${port}: ${probe.error.message}`,
      )
      continue
    }

    if (probe.status === 0) {
      return true
    }
  }

  return false
}

export function resolvePortProbeHosts(host: string): string[] {
  const normalizedHost = parseFrontendHost(host, 'TASKDECK_E2E_FRONTEND_HOST')
  if (normalizedHost.toLowerCase() !== 'localhost') {
    return [normalizedHost]
  }

  // Localhost can resolve to IPv4 or IPv6 depending on platform; probe both to avoid drift.
  return [normalizedHost, '127.0.0.1', '::1']
}

export function parseFrontendHost(rawHost: string, source: string): string {
  const normalizedHost = rawHost.trim()
  if (normalizedHost.length === 0) {
    throw new Error(`[e2e config] ${source} cannot be empty.`)
  }

  if (
    normalizedHost.includes('://') ||
    /[\s/?#'"`\\,;]/.test(normalizedHost)
  ) {
    throw new Error(
      `[e2e config] ${source} must be a hostname or IP literal without protocol/path/query delimiters. Received "${rawHost}".`,
    )
  }

  return normalizedHost
}

export function buildHttpOrigin(host: string, port: number): string {
  const normalizedHost = parseFrontendHost(host, 'TASKDECK_E2E_FRONTEND_HOST')
  const hostAuthority =
    normalizedHost.includes(':') && !normalizedHost.startsWith('[')
      ? `[${normalizedHost}]`
      : normalizedHost
  return `http://${hostAuthority}:${port}`
}

function runProbeScript(candidateHost: string, port: number, probeScript: string): ProbeResult {
  const probe = spawnSync(
    process.execPath,
    ['-e', probeScript, candidateHost, String(port), String(portProbeTimeoutMs)],
    {
      stdio: 'ignore',
      timeout: portProbeTimeoutMs + 50,
    },
  )

  return {
    error: probe.error ?? undefined,
    status: probe.status,
  }
}
