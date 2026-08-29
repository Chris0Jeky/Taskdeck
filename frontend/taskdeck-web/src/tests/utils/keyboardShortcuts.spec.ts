import { describe, expect, it } from 'vitest'
import { formatShortcut } from '../../utils/keyboardShortcuts'

describe('formatShortcut', () => {
  it('prefers userAgentData and renders Apple modifier glyphs', () => {
    const navigatorHints = {
      userAgentData: { platform: 'macOS' },
      userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)',
    }

    expect(formatShortcut('mod+k', navigatorHints)).toBe('⌘K')
    expect(formatShortcut('mod+shift+c', navigatorHints)).toBe('⌘⇧C')
  })

  it('renders Ctrl notation on non-Apple platforms', () => {
    const navigatorHints = {
      userAgentData: { platform: 'Windows' },
      userAgent: 'Mozilla/5.0 (Macintosh; Intel Mac OS X)',
    }

    expect(formatShortcut('mod+k', navigatorHints)).toBe('Ctrl+K')
    expect(formatShortcut('mod+shift+c', navigatorHints)).toBe('Ctrl+Shift+C')
  })

  it('falls back to userAgent when high-entropy platform data is unavailable', () => {
    expect(formatShortcut('mod+;', { userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS)' }))
      .toBe('⌘;')
  })

  it('safely defaults to Ctrl notation without a navigator', () => {
    expect(formatShortcut('mod+enter', null)).toBe('Ctrl+Enter')
  })

  it('normalizes legacy command-prefixed hints at shared button boundaries', () => {
    expect(formatShortcut('⌘;', { userAgentData: { platform: 'Windows' } })).toBe('Ctrl+;')
    expect(formatShortcut('⌘⏎', { userAgentData: { platform: 'macOS' } })).toBe('⌘⏎')
  })
})
