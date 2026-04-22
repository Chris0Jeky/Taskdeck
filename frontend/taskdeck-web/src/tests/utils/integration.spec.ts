import { describe, expect, it } from 'vitest'
import {
  normalizeConnectorType,
  normalizeConnectorDirection,
  normalizeConnectorStatus,
  normalizeConnectorEventType,
} from '../../types/integration'

describe('normalizeConnectorType', () => {
  it('maps numeric enum ordinals from the backend', () => {
    expect(normalizeConnectorType(0)).toBe('BrowserClipper')
    expect(normalizeConnectorType(1)).toBe('MarkdownImport')
    expect(normalizeConnectorType(2)).toBe('WebClip')
    expect(normalizeConnectorType(3)).toBe('GitHubIssueIntake')
    expect(normalizeConnectorType(4)).toBe('WebhookInbound')
    expect(normalizeConnectorType(5)).toBe('Custom')
  })

  it('normalizes case-insensitive string values', () => {
    expect(normalizeConnectorType('browserClipper' as any)).toBe('BrowserClipper')
    expect(normalizeConnectorType('WEBHOOKINBOUND' as any)).toBe('WebhookInbound')
    expect(normalizeConnectorType('custom' as any)).toBe('Custom')
  })

  it('passes through correctly-cased string values', () => {
    expect(normalizeConnectorType('BrowserClipper')).toBe('BrowserClipper')
    expect(normalizeConnectorType('GitHubIssueIntake')).toBe('GitHubIssueIntake')
  })

  it('falls back to Custom for unknown values', () => {
    expect(normalizeConnectorType(99 as any)).toBe('Custom')
    expect(normalizeConnectorType(-1 as any)).toBe('Custom')
    expect(normalizeConnectorType('unknown' as any)).toBe('Custom')
  })
})

describe('normalizeConnectorDirection', () => {
  it('maps numeric enum ordinals from the backend', () => {
    expect(normalizeConnectorDirection(0)).toBe('Inbound')
    expect(normalizeConnectorDirection(1)).toBe('Context')
    expect(normalizeConnectorDirection(2)).toBe('Outbound')
  })

  it('normalizes case-insensitive string values', () => {
    expect(normalizeConnectorDirection('inbound' as any)).toBe('Inbound')
    expect(normalizeConnectorDirection('OUTBOUND' as any)).toBe('Outbound')
  })

  it('passes through correctly-cased string values', () => {
    expect(normalizeConnectorDirection('Inbound')).toBe('Inbound')
    expect(normalizeConnectorDirection('Context')).toBe('Context')
  })

  it('falls back to Inbound for unknown values', () => {
    expect(normalizeConnectorDirection(99 as any)).toBe('Inbound')
    expect(normalizeConnectorDirection(-1 as any)).toBe('Inbound')
    expect(normalizeConnectorDirection('unknown' as any)).toBe('Inbound')
  })
})

describe('normalizeConnectorStatus', () => {
  it('maps numeric enum ordinals from the backend', () => {
    expect(normalizeConnectorStatus(0)).toBe('Active')
    expect(normalizeConnectorStatus(1)).toBe('Disabled')
    expect(normalizeConnectorStatus(2)).toBe('Error')
  })

  it('normalizes case-insensitive string values', () => {
    expect(normalizeConnectorStatus('active' as any)).toBe('Active')
    expect(normalizeConnectorStatus('DISABLED' as any)).toBe('Disabled')
    expect(normalizeConnectorStatus('error' as any)).toBe('Error')
  })

  it('passes through correctly-cased string values', () => {
    expect(normalizeConnectorStatus('Active')).toBe('Active')
    expect(normalizeConnectorStatus('Error')).toBe('Error')
  })

  it('falls back to Active for unknown values', () => {
    expect(normalizeConnectorStatus(99 as any)).toBe('Active')
    expect(normalizeConnectorStatus(-1 as any)).toBe('Active')
    expect(normalizeConnectorStatus('unknown' as any)).toBe('Active')
  })
})

describe('normalizeConnectorEventType', () => {
  it('maps numeric enum ordinals from the backend', () => {
    expect(normalizeConnectorEventType(0)).toBe('Connected')
    expect(normalizeConnectorEventType(1)).toBe('Disconnected')
    expect(normalizeConnectorEventType(2)).toBe('DataReceived')
    expect(normalizeConnectorEventType(3)).toBe('Error')
  })

  it('normalizes case-insensitive string values', () => {
    expect(normalizeConnectorEventType('connected' as any)).toBe('Connected')
    expect(normalizeConnectorEventType('DATARECEIVED' as any)).toBe('DataReceived')
    expect(normalizeConnectorEventType('error' as any)).toBe('Error')
  })

  it('passes through correctly-cased string values', () => {
    expect(normalizeConnectorEventType('Connected')).toBe('Connected')
    expect(normalizeConnectorEventType('Disconnected')).toBe('Disconnected')
  })

  it('falls back to Connected for unknown values', () => {
    expect(normalizeConnectorEventType(99 as any)).toBe('Connected')
    expect(normalizeConnectorEventType(-1 as any)).toBe('Connected')
    expect(normalizeConnectorEventType('unknown' as any)).toBe('Connected')
  })
})
