import net from 'node:net'
import http from 'node:http'
import { spawn } from 'node:child_process'
import path from 'node:path'

const defaultHost = process.env.TASKDECK_DEV_HOST ?? 'localhost'
const defaultPort = 5173
const fallbackPorts = [defaultPort, 4173, 5001]
const probeTimeoutMs = 300
const maxProbeResponseBytes = 64 * 1024
const frontendIdentityMarkers = ['<title>taskdeck-web</title>', '/src/main.ts']

const cliArgs = process.argv.slice(2)
const hostOption = readOptionValue(cliArgs, ['--host'])
const portOption = readOptionValue(cliArgs, ['--port', '-p'])
const hasBareHostOption = cliArgs.includes('--host')

const requestedHost = hasBareHostOption && !hostOption ? '0.0.0.0' : hostOption ?? defaultHost
const effectiveHost = parseHost(
  requestedHost,
  hostOption || hasBareHostOption ? 'CLI --host' : 'TASKDECK_DEV_HOST',
)
const effectivePort = portOption
  ? parsePort(portOption, 'CLI --port')
  : process.env.TASKDECK_DEV_PORT
    ? parsePort(process.env.TASKDECK_DEV_PORT, 'TASKDECK_DEV_PORT')
    : await resolveDefaultPort(effectiveHost)

const viteArgs = [...cliArgs]
if (!hasOption(cliArgs, ['--host'])) {
  viteArgs.push('--host', effectiveHost)
}

if (!hasOption(cliArgs, ['--port', '-p'])) {
  viteArgs.push('--port', String(effectivePort))
}

if (!hasOption(cliArgs, ['--strictPort'])) {
  viteArgs.push('--strictPort')
}

console.log(`[dev] starting Vite on ${buildHttpOrigin(effectiveHost, effectivePort)}`)

const viteCliPath = path.resolve(process.cwd(), 'node_modules', 'vite', 'bin', 'vite.js')
const child = spawn(process.execPath, [viteCliPath, ...viteArgs], {
  stdio: ['ignore', 'pipe', 'pipe'],
})

child.stdout?.on('data', (chunk) => {
  process.stdout.write(chunk)
})

child.stderr?.on('data', (chunk) => {
  process.stderr.write(chunk)
})

child.on('error', (error) => {
  console.error(`[dev] failed to launch Vite: ${error.message}`)
  process.exit(1)
})

child.on('exit', (code, signal) => {
  if (signal) {
    try {
      process.kill(process.pid, signal)
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error)
      console.error(`[dev] failed to forward Vite exit signal ${signal}: ${message}`)
      process.exit(1)
    }
    return
  }

  process.exit(code ?? 0)
})

function hasOption(args, names) {
  return args.some((arg) => names.includes(arg) || names.some((name) => arg.startsWith(`${name}=`)))
}

function readOptionValue(args, names) {
  for (let index = 0; index < args.length; index++) {
    const arg = args[index]
    for (const name of names) {
      if (arg === name) {
        if (index + 1 < args.length && !args[index + 1].startsWith('-')) {
          return args[index + 1]
        }

        continue
      }

      if (arg.startsWith(`${name}=`)) {
        return arg.slice(name.length + 1)
      }
    }
  }

  return undefined
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

async function resolveDefaultPort(host) {
  for (const candidatePort of fallbackPorts) {
    if (await canConnectToTaskdeckFrontend(host, candidatePort)) {
      console.warn(
        `[dev] detected existing Taskdeck frontend listener on ${buildHttpOrigin(host, candidatePort)}; ` +
          'skipping occupied port for new Vite process.',
      )
      continue
    }

    if (await canBindPort(host, candidatePort)) {
      return candidatePort
    }
  }

  console.warn(
    `[dev] could not find a bindable frontend port in ${fallbackPorts.join(', ')} for host "${host}". ` +
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
