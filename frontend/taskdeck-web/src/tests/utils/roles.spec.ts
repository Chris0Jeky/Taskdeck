import { describe, expect, it } from 'vitest'
import type { BoardRoleValue } from '../../types/access'
import { normalizeBoardRole, toBoardRoleValue } from '../../utils/roles'

describe('roles utils', () => {
  it('normalizes numeric roles from backend', () => {
    expect(normalizeBoardRole(1)).toBe('Admin')
  })

  it('normalizes case-insensitive string roles', () => {
    expect(normalizeBoardRole('owner' as BoardRoleValue)).toBe('Owner')
  })

  it('falls back to Viewer for unknown roles', () => {
    expect(normalizeBoardRole('invalid' as BoardRoleValue)).toBe('Viewer')
  })

  it('maps canonical role names to numeric role values', () => {
    expect(toBoardRoleValue('Editor')).toBe(2)
  })

  it('returns numeric role values unchanged', () => {
    expect(toBoardRoleValue(3)).toBe(3)
  })
})
