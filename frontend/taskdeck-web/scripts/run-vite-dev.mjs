import net from 'node:net'
import { spawn } from 'node:child_process'
import path from 'node:path'

const defaultHost = process.env.TASKDECK_DEV_HOST ?? 'localhost'
const defaultPort = 5173
const fallbackPorts = [defaultPort, 4173, 5001]

const cliArgs = process.argv.slice(2)
const hostOption = readOptionValue(cliArgs, ['--host'])
const portOption = readOptionValue(cliArgs, ['--port', '-p'])

const effectiveHost = hostOption ?? defaultHost
const effectivePort = portOption
  ? parsePort(portOption, defaultPort, 'CLI --port')
  : process.env.TASKDECK_DEV_PORT
    ? parsePort(process.env.TASKDECK_DEV_PORT, defaultPort, 'TASKDECK_DEV_PORT')
    : await resolveDefaultPort(effectiveHost)

const viteArgs = [...cliArgs]
if (!hasOption(cliArgs, ['--host'])) {
  viteArgs.push('--host', effectiveHost)
}

if (!hasOption(cliArgs, ['--port', '-p'])) {
  viteArgs.push('--port', String(effectivePort))
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

function parsePort(rawPort, fallbackPort, source) {
  const normalizedPort = rawPort.trim()
  if (!/^\d+$/.test(normalizedPort)) {
    throw new Error(`[dev] ${source} must be an integer between 1 and 65535. Received "${rawPort}".`)
  }

  const parsedPort = Number.parseInt(normalizedPort, 10)
  if (parsedPort < 1 || parsedPort > 65535) {
    throw new Error(`[dev] ${source} must be between 1 and 65535. Received "${rawPort}".`)
  }

  return Number.isNaN(parsedPort) ? fallbackPort : parsedPort
}

async function resolveDefaultPort(host) {
  for (const candidatePort of fallbackPorts) {
    if (await canConnectToPort(host, candidatePort)) {
      return candidatePort
    }
  }

  for (const candidatePort of fallbackPorts) {
    if (await canBindPort(host, candidatePort)) {
      return candidatePort
    }
  }

  return defaultPort
}

async function canConnectToPort(host, port) {
  for (const candidateHost of resolveProbeHosts(host)) {
    if (await canConnectToHostPort(candidateHost, port)) {
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
  if (host.toLowerCase() !== 'localhost') {
    return [host]
  }

  return [host, '127.0.0.1', '::1']
}

function canConnectToHostPort(host, port) {
  return new Promise((resolve) => {
    const socket = net.createConnection({ host, port })

    const finalize = (result) => {
      socket.removeAllListeners()
      socket.destroy()
      resolve(result)
    }

    socket.setTimeout(300)
    socket.once('connect', () => finalize(true))
    socket.once('timeout', () => finalize(false))
    socket.once('error', () => finalize(false))
  })
}

function canBindHostPort(host, port) {
  return new Promise((resolve) => {
    const server = net.createServer()

    const finalize = (result) => {
      server.removeAllListeners()
      try {
        server.close()
      } catch {
        // no-op: close may throw if server was never opened.
      }
      resolve(result)
    }

    server.once('error', () => finalize(false))
    server.listen(port, host, () => {
      server.close(() => finalize(true))
    })
  })
}
