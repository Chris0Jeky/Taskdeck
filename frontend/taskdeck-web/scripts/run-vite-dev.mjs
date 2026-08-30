import net from 'node:net'
import http from 'node:http'
import path from 'node:path'
import { pathToFileURL } from 'node:url'

import { transformDevEntryGraph } from './check-dev-entry-graph.mjs'

const defaultHost = 'localhost'
const defaultPort = 5173
const fallbackPorts = [defaultPort, 4173, 5001]
const probeTimeoutMs = 300
const maxProbeResponseBytes = 64 * 1024
const frontendIdentityMarkers = ['<title>taskdeck-web</title>', '/src/main.ts']
const readyMarker = 'TASKDECK_DEV_FRONTEND_READY'

export async function runViteDev({
  args = process.argv.slice(2),
  env = process.env,
  loadVite = () => import('vite'),
  transformEntryGraph = transformDevEntryGraph,
  resolveDefaultPortImpl = resolveDefaultPort,
  logger = console,
} = {}) {
  const cliOptions = parseViteServeArgs(args)
  configureViteDebug(cliOptions, env)

  const requestedHost =
    cliOptions.host === true
      ? '0.0.0.0'
      : cliOptions.host ?? env.TASKDECK_DEV_HOST ?? defaultHost
  const effectiveHost = parseHost(
    requestedHost,
    cliOptions.host !== undefined ? 'CLI --host' : 'TASKDECK_DEV_HOST',
  )
  const effectivePort =
    cliOptions.port !== undefined
      ? parsePort(String(cliOptions.port), 'CLI --port')
      : env.TASKDECK_DEV_PORT
        ? parsePort(env.TASKDECK_DEV_PORT, 'TASKDECK_DEV_PORT')
        : await resolveDefaultPortImpl(effectiveHost, { logger })

  logger.log(`[dev] starting Vite on ${buildHttpOrigin(effectiveHost, effectivePort)}`)

  const { createServer } = await loadVite()
  const server = await createServer(buildInlineConfig(cliOptions, effectiveHost, effectivePort))

  try {
    if (!server.httpServer) {
      throw new Error('[dev] Vite HTTP server is unavailable.')
    }

    await server.listen()
    const graph = await transformEntryGraph(server)
    const endpoint = readResolvedFrontendEndpoint(server.resolvedUrls)

    server.printUrls()
    logger.log(`[dev] transformed ${graph.moduleCount} modules from ${graph.entryUrl}`)
    server.bindCLIShortcuts({ print: true })
    logger.log(
      `${readyMarker} ${JSON.stringify({
        schemaVersion: 1,
        url: endpoint.url,
        port: endpoint.port,
      })}`,
    )

    return server
  } catch (error) {
    await server.close()
    throw error
  }
}

export function parseViteServeArgs(args) {
  const options = {}
  const positionals = []
  const valueOptions = new Map([
    ['--config', 'config'],
    ['-c', 'config'],
    ['--base', 'base'],
    ['--logLevel', 'logLevel'],
    ['-l', 'logLevel'],
    ['--configLoader', 'configLoader'],
    ['--filter', 'filter'],
    ['-f', 'filter'],
    ['--mode', 'mode'],
    ['-m', 'mode'],
    ['--port', 'port'],
    ['-p', 'port'],
  ])
  const optionalValueOptions = new Map([
    ['--host', 'host'],
    ['--open', 'open'],
    ['--debug', 'debug'],
    ['-d', 'debug'],
  ])
  const booleanOptions = new Map([
    ['--clearScreen', 'clearScreen'],
    ['--cors', 'cors'],
    ['--strictPort', 'strictPort'],
    ['--force', 'force'],
    ['--experimentalBundle', 'experimentalBundle'],
  ])

  for (let index = 0; index < args.length; index++) {
    const arg = args[index]

    if (arg === '--') {
      positionals.push(...args.slice(index + 1))
      break
    }

    const equalsIndex = arg.indexOf('=')
    const optionName = equalsIndex >= 0 ? arg.slice(0, equalsIndex) : arg
    const inlineValue = equalsIndex >= 0 ? arg.slice(equalsIndex + 1) : undefined

    if (valueOptions.has(optionName)) {
      const value = inlineValue ?? args[++index]
      if (value === undefined || value.startsWith('-')) {
        throw new Error(`[dev] ${optionName} requires a value.`)
      }
      options[valueOptions.get(optionName)] = value
      continue
    }

    if (optionalValueOptions.has(optionName)) {
      let value = inlineValue
      if (value === undefined && index + 1 < args.length && !args[index + 1].startsWith('-')) {
        value = args[++index]
      }
      options[optionalValueOptions.get(optionName)] = value ?? true
      continue
    }

    if (booleanOptions.has(optionName)) {
      let value = inlineValue
      if (value === undefined && /^(?:true|false)$/.test(args[index + 1] ?? '')) {
        value = args[++index]
      }
      options[booleanOptions.get(optionName)] = parseBooleanOption(optionName, value)
      continue
    }

    if (optionName.startsWith('--no-') && booleanOptions.has(`--${optionName.slice(5)}`)) {
      options[booleanOptions.get(`--${optionName.slice(5)}`)] = false
      continue
    }

    if (arg.startsWith('-')) {
      throw new Error(`[dev] unsupported Vite dev option ${JSON.stringify(arg)}.`)
    }

    positionals.push(arg)
  }

  if (positionals.length > 1) {
    throw new Error('[dev] Vite dev accepts at most one root directory.')
  }

  options.root = positionals[0]
  return options
}

function parseBooleanOption(optionName, rawValue) {
  if (rawValue === undefined || rawValue === 'true') {
    return true
  }
  if (rawValue === 'false') {
    return false
  }
  throw new Error(`[dev] ${optionName} must be true or false.`)
}

function configureViteDebug(options, env) {
  if (options.debug === undefined || options.debug === false) {
    return
  }

  const requestedDebug = options.debug === true ? '*' : options.debug
  const namespaces = String(requestedDebug)
    .split(',')
    .map((value) => `vite:${value}`)
    .join(',')
  env.DEBUG = env.DEBUG ? `${env.DEBUG},${namespaces}` : namespaces

  if (options.filter) {
    env.VITE_DEBUG_FILTER = options.filter
  }
}

function buildInlineConfig(options, host, port) {
  const inlineConfig = {
    server: {
      host,
      port,
      // Port selection happens before Vite starts. A bind race must fail rather
      // than silently move the server away from the URL reported to launchers,
      // unless the caller explicitly retained Vite's non-strict behavior.
      strictPort: options.strictPort ?? true,
    },
  }

  assignDefined(inlineConfig, 'root', options.root)
  assignDefined(inlineConfig, 'base', options.base === '0' ? '' : options.base)
  assignDefined(inlineConfig, 'mode', options.mode)
  assignDefined(inlineConfig, 'configFile', options.config)
  assignDefined(inlineConfig, 'configLoader', options.configLoader)
  assignDefined(inlineConfig, 'logLevel', options.logLevel)
  assignDefined(inlineConfig, 'clearScreen', options.clearScreen)
  assignDefined(inlineConfig.server, 'open', options.open)
  assignDefined(inlineConfig.server, 'cors', options.cors)
  assignDefined(inlineConfig, 'forceOptimizeDeps', options.force)

  if (options.experimentalBundle !== undefined) {
    inlineConfig.experimental = { bundledDev: options.experimentalBundle }
  }

  return inlineConfig
}

function assignDefined(target, key, value) {
  if (value !== undefined) {
    target[key] = value
  }
}

export function readResolvedFrontendEndpoint(resolvedUrls) {
  const resolvedUrl = resolvedUrls?.local?.[0] ?? resolvedUrls?.network?.[0]
  if (!resolvedUrl) {
    throw new Error('[dev] Vite did not report a resolved frontend URL.')
  }

  let parsedUrl
  try {
    parsedUrl = new URL(resolvedUrl)
  } catch {
    throw new Error(`[dev] Vite reported an invalid frontend URL: ${JSON.stringify(resolvedUrl)}.`)
  }

  const port = parsedUrl.port
    ? Number.parseInt(parsedUrl.port, 10)
    : parsedUrl.protocol === 'http:'
      ? 80
      : parsedUrl.protocol === 'https:'
        ? 443
        : Number.NaN
  if (!Number.isSafeInteger(port) || port < 1 || port > 65535) {
    throw new Error(`[dev] Vite resolved URL has no valid listening port: ${JSON.stringify(resolvedUrl)}.`)
  }

  return { url: resolvedUrl, port }
}

function parsePort(rawPort, source) {
  const normalizedPort = rawPort.trim()
  if (!/^\d+$/.test(normalizedPort)) {
    throw new Error(`[dev] ${source} must be an integer between 1 and 65535. Received "${rawPort}".`)
  }

  const parsedPort = Number.parseInt(normalizedPort, 10)
  if (parsedPort < 1 || parsedPort > 65535) {
    throw new Error(`[dev] ${source} must be between 1 and 65535. Received "${rawPort}".`)
  }

  return parsedPort
}

export async function resolveDefaultPort(
  host,
  {
    candidatePorts = fallbackPorts,
    servesFrontend = canConnectToTaskdeckFrontend,
    canBind = canBindPort,
    logger = console,
  } = {},
) {
  for (const candidatePort of candidatePorts) {
    if (await servesFrontend(host, candidatePort)) {
      logger.warn(
        `[dev] detected existing Taskdeck frontend listener on ${buildHttpOrigin(host, candidatePort)}; ` +
          'skipping occupied port for new Vite process.',
      )
      continue
    }

    if (await canBind(host, candidatePort)) {
      return candidatePort
    }
  }

  logger.warn(
    `[dev] could not find a bindable frontend port in ${candidatePorts.join(', ')} for host "${host}". ` +
      `Falling back to ${defaultPort}. If startup fails, set TASKDECK_DEV_PORT explicitly.`,
  )
  return defaultPort
}

async function canConnectToTaskdeckFrontend(host, port) {
  for (const candidateHost of resolveProbeHosts(host)) {
    if (await servesTaskdeckFrontend(candidateHost, port)) {
      return true
    }
  }

  return false
}

async function canBindPort(host, port) {
  const normalizedHost = parseHost(host, 'TASKDECK_DEV_HOST')
  return canBindHostPort(normalizedHost, port)
}

function resolveProbeHosts(host) {
  const normalizedHost = parseHost(host, 'TASKDECK_DEV_HOST')
  if (normalizedHost.toLowerCase() !== 'localhost') {
    return [normalizedHost]
  }

  return [normalizedHost, '127.0.0.1', '::1']
}

function parseHost(rawHost, source) {
  let normalizedHost = rawHost.trim()
  let hadBrackets = false
  if (normalizedHost.startsWith('[') || normalizedHost.endsWith(']')) {
    if (!(normalizedHost.startsWith('[') && normalizedHost.endsWith(']'))) {
      throw new Error(
        `[dev] ${source} must be a hostname or IP literal without protocol/path/query delimiters. Received "${rawHost}".`,
      )
    }

    hadBrackets = true
    normalizedHost = normalizedHost.slice(1, -1)
  }

  if (normalizedHost.length === 0) {
    throw new Error(`[dev] ${source} cannot be empty.`)
  }

  if (
    normalizedHost.includes('://') ||
    normalizedHost.includes('[') ||
    normalizedHost.includes(']') ||
    /[\u0000-\u001F\u007F]/.test(normalizedHost) ||
    /[\s/?#'"`\\,;@]/u.test(normalizedHost)
  ) {
    throw new Error(
      `[dev] ${source} must be a hostname or IP literal without protocol/path/query delimiters. Received "${rawHost}".`,
    )
  }

  if (normalizedHost.includes(':') && !hadBrackets && net.isIP(normalizedHost) !== 6) {
    throw new Error(
      `[dev] ${source} must be a hostname or IP literal without protocol/path/query delimiters. Received "${rawHost}".`,
    )
  }

  return normalizedHost
}

function buildHttpOrigin(host, port) {
  const normalizedHost = parseHost(host, 'TASKDECK_DEV_HOST')
  const hostAuthority =
    normalizedHost.includes(':') && !normalizedHost.startsWith('[')
      ? `[${normalizedHost}]`
      : normalizedHost
  return `http://${hostAuthority}:${port}`
}

function servesTaskdeckFrontend(host, port) {
  return new Promise((resolve) => {
    let settled = false
    let responseText = ''
    let observedBytes = 0

    const settle = (result) => {
      if (settled) {
        return
      }

      settled = true
      clearTimeout(timeoutHandle)
      request.removeAllListeners()
      request.destroy()
      resolve(result)
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
        response.setEncoding('utf8')

        response.on('data', (chunk) => {
          if (settled) {
            return
          }

          observedBytes += Buffer.byteLength(chunk)
          if (observedBytes <= maxProbeResponseBytes) {
            responseText += chunk
          }

          const statusCode = response.statusCode ?? 0
          const hasExpectedIdentity = frontendIdentityMarkers.every((marker) => responseText.includes(marker))
          if (statusCode === 200 && hasExpectedIdentity) {
            response.destroy()
            settle(true)
            return
          }

          if (observedBytes > maxProbeResponseBytes) {
            response.destroy()
            settle(false)
          }
        })

        response.on('error', () => settle(false))
        response.on('end', () => {
          const statusCode = response.statusCode ?? 0
          const hasExpectedIdentity = frontendIdentityMarkers.every((marker) => responseText.includes(marker))
          settle(statusCode === 200 && hasExpectedIdentity)
        })
      },
    )

    const timeoutHandle = setTimeout(() => settle(false), probeTimeoutMs)
    request.on('error', () => settle(false))
    request.end()
  })
}

function canBindHostPort(host, port) {
  return new Promise((resolve) => {
    const server = net.createServer()
    let settled = false

    const finalize = (result) => {
      if (settled) {
        return
      }

      settled = true
      clearTimeout(timeoutHandle)
      server.removeAllListeners()
      try {
        server.close()
      } catch {
        // no-op: close may throw if server was never opened.
      }
      resolve(result)
    }

    const timeoutHandle = setTimeout(() => finalize(false), probeTimeoutMs)
    server.once('error', () => finalize(false))
    server.listen(port, host, () => {
      server.close(() => finalize(true))
    })
  })
}

function isDirectExecution() {
  if (!process.argv[1]) {
    return false
  }

  return import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href
}

if (isDirectExecution()) {
  try {
    await runViteDev()
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error)
    console.error(`[dev] failed to start usable Vite frontend: ${message}`)
    process.exitCode = 1
  }
}
