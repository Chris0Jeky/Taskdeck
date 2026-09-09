import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
const source = readFileSync(resolve(fileURLToPath(import.meta.url), '..', '..', 'src/grove-tokens.css'), 'utf8')

function luminance(hex: string): number {
  const channels = [1, 3, 5].map((offset) => {
    const value = parseInt(hex.slice(offset, offset + 2), 16) / 255
    return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4
  })
  return channels[0]! * 0.2126 + channels[1]! * 0.7152 + channels[2]! * 0.0722
}

describe('Grove text contrast', () => {
  for (const selector of ['body.paper.grove', 'body.paper-night.grove-night']) {
    const block = source.slice(source.indexOf(selector)).split('}')[0]!
    const tokens = Object.fromEntries([...block.matchAll(/(--[\w-]+):\s*(#[0-9a-f]{6});/g)].map((match) => [match[1], match[2]]))
    const ratio = (foreground: string, background: string) => {
      const a = luminance(tokens[foreground]!), b = luminance(tokens[background]!)
      return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05)
    }
    it(`${selector} clears AA for small text on workspace surfaces`, () => {
      for (const foreground of ['--ink', '--ink-2', '--mute', '--faint']) {
        for (const background of ['--paper', '--paper-2', '--paper-card', '--paper-edge']) {
          expect(ratio(foreground, background), `${foreground} on ${background}`).toBeGreaterThanOrEqual(4.5)
        }
      }
      expect(ratio('--td-on-ember', '--ember')).toBeGreaterThanOrEqual(4.5)
      expect(ratio('--ember-ink', '--ember-tint')).toBeGreaterThanOrEqual(4.5)
    })
  }
})
