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

const effectiveHost = parseHost(hostOption ?? defaultHost, hostOption ? 'CLI --host' : 'TASKDECK_DEV_HOST')
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

console.log(`[dev] starting Vite on http://${effectiveHost}:${effectivePort}`)

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
    process.kill(process.pid, signal)
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
      if (arg === name && index + 1 < args.length) {
        return args[index + 1]
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
      return candidatePort
    }
  }

  for (const candidatePort of fallbackPorts) {
    if (await canBindPort(host, candidatePort)) {
      return candidatePort
    }
  }

  console.warn(
    `[dev] could not find a running Taskdeck frontend or bindable port in ${fallbackPorts.join(', ')} for host "${host}". ` +
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
  for (const candidateHost of resolveProbeHosts(host)) {
    if (await canBindHostPort(candidateHost, port)) {
      return true
    }
  }

  return false
}

function resolveProbeHosts(host) {
  const normalizedHost = parseHost(host, 'TASKDECK_DEV_HOST')
  if (normalizedHost.toLowerCase() !== 'localhost') {
    return [normalizedHost]
  }

  return [normalizedHost, '127.0.0.1', '::1']
}

function parseHost(rawHost, source) {
  const normalizedHost = rawHost.trim()
  if (normalizedHost.length === 0) {
    throw new Error(`[dev] ${source} cannot be empty.`)
  }

  if (
    normalizedHost.includes('://') ||
    /[\u0000-\u001F\u007F]/.test(normalizedHost) ||
    /[\s/?#'"`\\,;]/u.test(normalizedHost)
  ) {
    throw new Error(
      `[dev] ${source} must be a hostname or IP literal without protocol/path/query delimiters. Received "${rawHost}".`,
    )
  }

  return normalizedHost
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
