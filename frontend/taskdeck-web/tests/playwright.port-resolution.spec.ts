import { describe, expect, it, vi } from 'vitest'
import {
  buildHttpOrigin,
  defaultFrontendPort,
  parseFrontendHost,
  resolveDefaultFrontendPort,
  resolvePortProbeHosts,
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
  })

  it('rejects unsafe frontend host values', () => {
    expect(() => parseFrontendHost('http://localhost', 'TASKDECK_E2E_FRONTEND_HOST')).toThrow()
    expect(() => parseFrontendHost('localhost/path', 'TASKDECK_E2E_FRONTEND_HOST')).toThrow()
    expect(() => parseFrontendHost('local host', 'TASKDECK_E2E_FRONTEND_HOST')).toThrow()
    expect(() => parseFrontendHost('   ', 'TASKDECK_E2E_FRONTEND_HOST')).toThrow()
  })

  it('formats IPv6 origins with bracketed authority', () => {
    expect(buildHttpOrigin('::1', 5173)).toBe('http://[::1]:5173')
  })
})
