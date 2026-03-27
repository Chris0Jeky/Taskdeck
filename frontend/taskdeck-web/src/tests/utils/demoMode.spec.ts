import { describe, expect, it, beforeEach } from 'vitest'
import {
  isDemoSessionActive,
  activateDemoSession,
  clearDemoSession,
  DEMO_USER,
} from '../../utils/demoMode'

describe('demoMode', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  describe('DEMO_USER', () => {
    it('has expected identity fields', () => {
      expect(DEMO_USER.id).toBe('demo-user-0000-0000-000000000000')
      expect(DEMO_USER.username).toBe('demo')
      expect(DEMO_USER.email).toBe('demo@taskdeck.local')
      expect(typeof DEMO_USER.defaultRole).toBe('number')
    })
  })

  describe('demo session lifecycle', () => {
    it('is not active by default', () => {
      expect(isDemoSessionActive()).toBe(false)
    })

    it('becomes active after activation', () => {
      activateDemoSession()
      expect(localStorage.getItem('taskdeck_demo')).toBe('1')
    })

    it('clears the session', () => {
      activateDemoSession()
      clearDemoSession()
      expect(localStorage.getItem('taskdeck_demo')).toBeNull()
    })
  })
})
