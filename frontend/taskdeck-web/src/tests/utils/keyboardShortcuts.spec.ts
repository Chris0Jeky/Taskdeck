import { describe, expect, it } from 'vitest'
import {
  APP_SHELL_SHORTCUT_BINDINGS,
  formatShortcut,
  KEYBOARD_HELP_SHORTCUT,
  strokeMatches,
} from '../../utils/keyboardShortcuts'

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

  it('keeps a reduced non-Apple user agent on Ctrl even when legacy platform data exists', () => {
    // Tier 2 recognises the reduced Windows UA, so the deprecated tier is never
    // reached and a stale/spoofed `platform` cannot flip the notation.
    expect(formatShortcut('mod+k', {
      userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36',
      platform: 'MacIntel',
    })).toBe('Ctrl+K')
  })

  it('uses the feature-detected legacy platform only when the first two tiers are generic', () => {
    expect(formatShortcut('mod+k', { userAgent: 'Mozilla/5.0', platform: 'MacIntel' }))
      .toBe('⌘K')
    expect(formatShortcut('mod+k', { platform: 'MacIntel' })).toBe('⌘K')
    expect(formatShortcut('mod+k', { platform: 'iPhone' })).toBe('⌘K')
    expect(formatShortcut('mod+k', { userAgent: 'Mozilla/5.0', platform: 'Win32' }))
      .toBe('Ctrl+K')
  })

  it('does not throw when the legacy platform property is missing or unusable', () => {
    expect(formatShortcut('mod+k', { userAgent: 'Mozilla/5.0' })).toBe('Ctrl+K')
    expect(formatShortcut('mod+k', { platform: '   ' })).toBe('Ctrl+K')
    expect(formatShortcut('mod+k', {
      platform: undefined as unknown as string,
    })).toBe('Ctrl+K')
  })

  it('safely defaults to Ctrl notation without a navigator', () => {
    expect(formatShortcut('mod+enter', null)).toBe('Ctrl+Enter')
  })

  it('normalizes legacy command-prefixed hints at shared button boundaries', () => {
    expect(formatShortcut('⌘;', { userAgentData: { platform: 'Windows' } })).toBe('Ctrl+;')
    expect(formatShortcut('⌘⏎', { userAgentData: { platform: 'macOS' } })).toBe('⌘⏎')
  })
})

describe('strokeMatches', () => {
  function keydown(init: KeyboardEventInit): KeyboardEvent {
    return new KeyboardEvent('keydown', init)
  }

  const homeStroke = { key: 'h' } as const
  const paletteStroke = { key: 'k', mod: true } as const
  const captureStroke = { key: 'c', mod: true, shift: true } as const
  const helpStroke = KEYBOARD_HELP_SHORTCUT.sequence[0]!

  it('matches a bare letter stroke with no modifiers held', () => {
    expect(strokeMatches(keydown({ key: 'h' }), homeStroke)).toBe(true)
  })

  it('does not match a bare letter stroke while Shift is held', () => {
    // The comparison is case-insensitive, so `Shift+H` arrives as `H` and used
    // to navigate Home (#1968).
    expect(strokeMatches(keydown({ key: 'H', shiftKey: true }), homeStroke)).toBe(false)
    expect(strokeMatches(keydown({ key: 'h', shiftKey: true }), homeStroke)).toBe(false)
    expect(strokeMatches(keydown({ key: 'K', ctrlKey: true, shiftKey: true }), paletteStroke))
      .toBe(false)
  })

  it('keeps a declared shift exact in both directions', () => {
    expect(strokeMatches(keydown({ key: 'C', ctrlKey: true, shiftKey: true }), captureStroke))
      .toBe(true)
    expect(strokeMatches(keydown({ key: 'c', ctrlKey: true }), captureStroke)).toBe(false)
  })

  it('keeps mod and alt exact over letter strokes', () => {
    expect(strokeMatches(keydown({ key: 'h', altKey: true }), homeStroke)).toBe(false)
    expect(strokeMatches(keydown({ key: 'h', ctrlKey: true }), homeStroke)).toBe(false)
    expect(strokeMatches(keydown({ key: 'k', ctrlKey: true }), paletteStroke)).toBe(true)
    expect(strokeMatches(keydown({ key: 'k', metaKey: true }), paletteStroke)).toBe(true)
    expect(strokeMatches(keydown({ key: 'k' }), paletteStroke)).toBe(false)
    expect(strokeMatches(keydown({ key: 'k', ctrlKey: true, altKey: true }), paletteStroke))
      .toBe(false)
  })

  it('reaches the ? help stroke on layouts that need Shift or AltGr', () => {
    expect(strokeMatches(keydown({ key: '?' }), helpStroke)).toBe(true)
    // The common case: `?` is the shifted character, so Shift is always down.
    expect(strokeMatches(keydown({ key: '?', shiftKey: true }), helpStroke)).toBe(true)
    // AltGr reports altKey, and on Windows ctrlKey with it (#1968).
    expect(strokeMatches(keydown({ key: '?', altKey: true }), helpStroke)).toBe(true)
    expect(strokeMatches(keydown({ key: '?', altKey: true, ctrlKey: true }), helpStroke)).toBe(true)
  })

  it('still refuses a real Ctrl or Command chord over the ? help stroke', () => {
    expect(strokeMatches(keydown({ key: '?', ctrlKey: true }), helpStroke)).toBe(false)
    expect(strokeMatches(keydown({ key: '?', metaKey: true }), helpStroke)).toBe(false)
  })

  it('matches every app-shell binding against the stroke it advertises', () => {
    expect(APP_SHELL_SHORTCUT_BINDINGS.length).toBeGreaterThan(0)

    for (const binding of APP_SHELL_SHORTCUT_BINDINGS) {
      for (const stroke of binding.sequence) {
        const event = keydown({
          key: stroke.key,
          ctrlKey: stroke.mod === true,
          shiftKey: stroke.shift === true,
          altKey: stroke.alt === true,
        })
        expect({ id: binding.id, key: stroke.key, matches: strokeMatches(event, stroke) })
          .toEqual({ id: binding.id, key: stroke.key, matches: true })
      }
    }
  })
})
