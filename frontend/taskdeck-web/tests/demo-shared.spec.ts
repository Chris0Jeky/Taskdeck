import { describe, expect, it } from 'vitest'

import { assertSafeLocalApiTarget } from '../scripts/demo-shared.mjs'

describe('demo api target safety guard', () => {
  it('accepts local demo api targets', () => {
    expect(() => {
      assertSafeLocalApiTarget('http://localhost:5000/api', {
        contextLabel: 'run demo director',
      })
    }).not.toThrow()
  })

  it('rejects non-local demo api targets by default', () => {
    expect(() => {
      assertSafeLocalApiTarget('http://demo.taskdeck.example/api', {
        contextLabel: 'run demo director',
      })
    }).toThrow('Refusing to run demo director against non-local API target')
  })

  it('allows non-local demo api targets only when explicitly overridden', () => {
    expect(() => {
      assertSafeLocalApiTarget('http://demo.taskdeck.example/api', {
        allowNonLocal: true,
        contextLabel: 'run demo director',
      })
    }).not.toThrow()
  })
})
