import { describe, expect, it, vi } from 'vitest'
import {
  buildHttpOrigin,
  canBindPort,
  canConnectToTaskdeckFrontend,
  defaultFrontendPort,
  maxProbeResponseBytes,
  parseFrontendHost,
  resolveDefaultFrontendPort,
  resolvePortProbeHosts,
  taskdeckFrontendIdentityMarkers,
} from '../playwright.port-resolution'

describe('playwright frontend port resolution', () => {
  it('prefers a running Taskdeck frontend when a candidate port matches identity', () => {
    const connectProbe = vi.fn((_: string, port: number) => port === 4173)
    const bindProbe = vi.fn(() => true)

    const resolvedPort = resolveDefaultFrontendPort('localhost', {
      connectProbe,
      bindProbe,
      fallbackPorts: [5173, 4173, 5001],
    })

    expect(resolvedPort).toBe(4173)
    expect(connectProbe).toHaveBeenCalledTimes(2)
    expect(bindProbe).not.toHaveBeenCalled()
  })

  it('falls back to first bindable candidate when no running frontend is detected', () => {
    const connectProbe = vi.fn(() => false)
    const bindProbe = vi.fn((_: string, port: number) => port === 5001)

    const resolvedPort = resolveDefaultFrontendPort('localhost', {
      connectProbe,
      bindProbe,
      fallbackPorts: [5173, 4173, 5001],
    })

    expect(resolvedPort).toBe(5001)
    expect(connectProbe).toHaveBeenCalledTimes(3)
    expect(bindProbe).toHaveBeenCalledTimes(3)
  })

  it('prefers bindable candidates when existing frontend reuse is disabled', () => {
    const connectProbe = vi.fn(() => true)
    const bindProbe = vi.fn((_: string, port: number) => port === 5173)

    const resolvedPort = resolveDefaultFrontendPort('localhost', {
      allowExistingFrontendReuse: false,
      connectProbe,
      bindProbe,
      fallbackPorts: [5173, 4173, 5001],
    })

    expect(resolvedPort).toBe(5173)
    expect(connectProbe).not.toHaveBeenCalled()
    expect(bindProbe).toHaveBeenCalledTimes(1)
  })

  it('returns default port and emits fallback diagnostics when no candidate resolves', () => {
    const warnings: string[] = []

    const resolvedPort = resolveDefaultFrontendPort('localhost', {
      connectProbe: () => false,
      bindProbe: () => false,
      fallbackPorts: [5173, 4173, 5001],
      onFallback: (message) => warnings.push(message),
    })

    expect(resolvedPort).toBe(defaultFrontendPort)
    expect(warnings).toHaveLength(1)
    expect(warnings[0]).toContain('5173, 4173, 5001')
    expect(warnings[0]).toContain('TASKDECK_E2E_FRONTEND_PORT')
  })

  it('normalizes localhost probe hosts for both IPv4 and IPv6 loopback paths', () => {
    expect(resolvePortProbeHosts('localhost')).toEqual(['localhost', '127.0.0.1', '::1'])
    expect(resolvePortProbeHosts('127.0.0.1')).toEqual(['127.0.0.1'])
    expect(resolvePortProbeHosts('[::1]')).toEqual(['::1'])
  })

  it('rejects unsafe frontend host values', () => {
    expect(() => parseFrontendHost('http://localhost', 'TASKDECK_E2E_FRONTEND_HOST')).toThrow()
    expect(() => parseFrontendHost('localhost/path', 'TASKDECK_E2E_FRONTEND_HOST')).toThrow()
    expect(() => parseFrontendHost('local host', 'TASKDECK_E2E_FRONTEND_HOST')).toThrow()
    expect(() => parseFrontendHost('user@localhost', 'TASKDECK_E2E_FRONTEND_HOST')).toThrow()
    expect(() => parseFrontendHost('localhost:5173', 'TASKDECK_E2E_FRONTEND_HOST')).toThrow()
    expect(() => parseFrontendHost('localhost\u0000', 'TASKDECK_E2E_FRONTEND_HOST')).toThrow()
    expect(() => parseFrontendHost('   ', 'TASKDECK_E2E_FRONTEND_HOST')).toThrow()
  })

  it('formats IPv6 origins with bracketed authority', () => {
    expect(buildHttpOrigin('::1', 5173)).toBe('http://[::1]:5173')
    expect(buildHttpOrigin('[::1]', 5173)).toBe('http://[::1]:5173')
    expect(parseFrontendHost('[::1]', 'TASKDECK_E2E_FRONTEND_HOST')).toBe('::1')
  })

  it('connect probe reports spawn failures and still resolves when a later host succeeds', () => {
    const errors: string[] = []
    const probeCalls: string[] = []

    const result = canConnectToTaskdeckFrontend('localhost', 4173, {
      onProbeError: (message) => errors.push(message),
      probeRunner: (candidateHost, _port, probeScript) => {
        probeCalls.push(candidateHost)

        if (candidateHost === 'localhost') {
          expect(probeScript).toContain(
            `const markers = ${JSON.stringify(taskdeckFrontendIdentityMarkers)}`,
          )
          expect(probeScript).toContain(
            `const maxProbeResponseBytes = ${String(maxProbeResponseBytes)}`,
          )
          expect(probeScript).toContain('markers.every((marker) => responseText.includes(marker))')
          expect(probeScript).toContain("response.on('error'")
          return { error: new Error('spawn failed'), status: null }
        }

        if (candidateHost === '127.0.0.1') {
          return { status: 1 }
        }

        return { status: 0 }
      },
    })

    expect(result).toBe(true)
    expect(probeCalls).toEqual(['localhost', '127.0.0.1', '::1'])
    expect(errors).toHaveLength(1)
    expect(errors[0]).toContain('frontend identity probe spawn failed')
    expect(errors[0]).toContain('localhost:4173')
  })

  it('bind probe checks only requested host and reports spawn errors', () => {
    const errors: string[] = []
    const probeCalls: string[] = []

    const result = canBindPort('localhost', 5001, {
      onProbeError: (message) => errors.push(message),
      probeRunner: (candidateHost) => {
        probeCalls.push(candidateHost)
        return { error: new Error('bind probe spawn failed'), status: null }
      },
    })

    expect(result).toBe(false)
    expect(probeCalls).toEqual(['localhost'])
    expect(errors).toHaveLength(1)
    expect(errors[0]).toContain('frontend bind probe spawn failed')
    expect(errors[0]).toContain('localhost:5001')
  })

  it('reports signal-terminated probe executions', () => {
    const errors: string[] = []

    const result = canConnectToTaskdeckFrontend('127.0.0.1', 5173, {
      onProbeError: (message) => errors.push(message),
      probeRunner: () => ({ status: null, signal: 'SIGTERM' }),
    })

    expect(result).toBe(false)
    expect(errors).toHaveLength(1)
    expect(errors[0]).toContain('terminated by signal SIGTERM')
  })
})
