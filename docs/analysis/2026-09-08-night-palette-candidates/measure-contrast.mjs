import { readFileSync, writeFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const reportDir = dirname(fileURLToPath(import.meta.url))
const repoRoot = resolve(reportDir, '..', '..', '..')
const config = JSON.parse(readFileSync(resolve(reportDir, 'palettes.json'), 'utf8'))
const css = readFileSync(resolve(repoRoot, config.source), 'utf8')
const block = css.match(/\.paper-night\s*\{([\s\S]*?)\n\}/)?.[1]
if (!block) throw new Error('Could not locate .paper-night in paper-tokens.css')

const baseline = Object.fromEntries(
  [...block.matchAll(/(--[a-z0-9-]+):\s*(#[0-9a-fA-F]{3,8})/g)]
    .map((match) => [match[1], match[2].toLowerCase()]),
)
const variants = config.variants.map((variant) => ({
  ...variant,
  tokens: { ...baseline, ...variant.overrides },
}))

function hexToRgb(hex) {
  let value = hex.replace('#', '')
  if (value.length === 3) value = value.split('').map((channel) => channel + channel).join('')
  if (value.length === 8) value = value.slice(0, 6)
  return [0, 2, 4].map((offset) => Number.parseInt(value.slice(offset, offset + 2), 16))
}

// Same WCAG sRGB calculation as the existing frontend contrast specs.
function luminance(hex) {
  const linear = hexToRgb(hex).map((channel) => {
    const value = channel / 255
    return value <= 0.03928 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4
  })
  return 0.2126 * linear[0] + 0.7152 * linear[1] + 0.0722 * linear[2]
}

function contrast(first, second) {
  const values = [luminance(first), luminance(second)].sort((left, right) => right - left)
  return (values[0] + 0.05) / (values[1] + 0.05)
}

function brighten(hex, factor) {
  const channels = hexToRgb(hex).map((channel) => Math.min(255, Math.round(channel * factor)))
  return '#' + channels.map((channel) => channel.toString(16).padStart(2, '0')).join('')
}

function rounded(value) {
  return Math.round(value * 100) / 100
}

const expectedBaseline = {
  '--line/--paper-card': 1.15,
  '--line-soft/--paper-card': 1.05,
  '--line/--paper': 1.23,
  '--paper-card/--paper': 1.07,
  '--paper-card/--paper-2': 1.10,
  '--whisper/--paper-card': 1.48,
  '--ink/--paper': 14.88,
  '--ink/--paper-card': 13.96,
  '--ink-2/--paper-card': 9.65,
  '--mute/--paper-card': 5.54,
  '--faint/--paper-card': 4.79
}

for (const [pair, expected] of Object.entries(expectedBaseline)) {
  const [foreground, background] = pair.split('/')
  const actual = rounded(contrast(baseline[foreground], baseline[background]))
  if (actual !== expected) throw new Error('Baseline drift for ' + pair + ': ' + actual)
}

const stableTokens = [
  '--ink', '--ink-deep', '--ink-2', '--mute',
  '--ember', '--ember-deep', '--ember-bloom', '--ember-tint',
  '--ember-ink', '--td-on-ember', '--applied', '--applied-tint',
  '--overdue', '--overdue-tint'
]
for (const variant of variants.slice(1)) {
  for (const token of stableTokens) {
    if (variant.tokens[token] !== baseline[token]) {
      throw new Error(variant.id + ' unexpectedly changes ' + token)
    }
  }
}

const surfaces = ['--paper', '--paper-2', '--paper-card', '--paper-edge']
const foregrounds = [
  '--ink', '--ink-deep', '--ink-2', '--mute', '--faint', '--whisper',
  '--ember', '--ember-deep', '--ember-ink', '--applied', '--overdue',
  '--line', '--line-soft'
]
const pairs = []
for (const foreground of foregrounds) {
  for (const background of surfaces) {
    pairs.push({
      category: 'foreground / surface',
      pair: foreground + ' / ' + background,
      measure: (tokens) => contrast(tokens[foreground], tokens[background])
    })
  }
}
pairs.push(
  {
    category: 'surface hierarchy',
    pair: '--paper-card / --paper',
    measure: (tokens) => contrast(tokens['--paper-card'], tokens['--paper'])
  },
  {
    category: 'surface hierarchy',
    pair: '--paper-card / --paper-2',
    measure: (tokens) => contrast(tokens['--paper-card'], tokens['--paper-2'])
  },
  {
    category: 'surface hierarchy',
    pair: '--paper-edge / --paper',
    measure: (tokens) => contrast(tokens['--paper-edge'], tokens['--paper'])
  },
  {
    category: 'control text',
    pair: '--td-on-ember / --ember',
    measure: (tokens) => contrast(tokens['--td-on-ember'], tokens['--ember'])
  },
  {
    category: 'control text',
    pair: '--td-on-ember / brightness(1.1) --ember',
    measure: (tokens) => contrast(tokens['--td-on-ember'], brighten(tokens['--ember'], 1.1))
  },
  {
    category: 'status text',
    pair: '--ember-ink / --ember-tint',
    measure: (tokens) => contrast(tokens['--ember-ink'], tokens['--ember-tint'])
  }
)

for (const variant of variants) {
  for (const foreground of ['--ink', '--ink-2', '--mute', '--faint']) {
    for (const background of ['--paper', '--paper-2', '--paper-card']) {
      const ratio = contrast(variant.tokens[foreground], variant.tokens[background])
      if (ratio < 4.5) {
        throw new Error(variant.id + ': ' + foreground + ' / ' + background + ' is ' + ratio.toFixed(2))
      }
    }
  }
  for (const foreground of ['--ember', '--applied', '--overdue']) {
    const ratio = contrast(variant.tokens[foreground], variant.tokens['--paper-card'])
    if (ratio < 4.5) {
      throw new Error(variant.id + ': ' + foreground + ' / --paper-card is ' + ratio.toFixed(2))
    }
  }
}

const csvRows = [
  ['category', 'pair', ...variants.map((variant) => variant.label)],
  ...pairs.map((pair) => [
    pair.category,
    pair.pair,
    ...variants.map((variant) => pair.measure(variant.tokens).toFixed(2))
  ])
]
const csv = csvRows
  .map((row) => row.map((value) => '"' + String(value).replaceAll('"', '""') + '"').join(','))
  .join('\n')
writeFileSync(resolve(reportDir, 'contrast-matrix.csv'), csv + '\n')

const reportPairs = [
  ['Boundary', '--line / --paper-card'],
  ['Soft boundary', '--line-soft / --paper-card'],
  ['Boundary on substrate', '--line / --paper'],
  ['Card step', '--paper-card / --paper'],
  ['Card step on paper 2', '--paper-card / --paper-2'],
  ['Whisper on card', '--whisper / --paper-card'],
  ['Primary ink on substrate', '--ink / --paper'],
  ['Primary ink on card', '--ink / --paper-card'],
  ['Secondary ink on card', '--ink-2 / --paper-card'],
  ['Muted ink on card', '--mute / --paper-card'],
  ['Faint ink on card', '--faint / --paper-card']
]
const header = '| Measure | ' + variants.map((variant) => variant.label).join(' | ') + ' |'
const rule = '| --- | ' + variants.map(() => '---:').join(' | ') + ' |'
const rows = reportPairs.map(([label, key]) => {
  const pair = pairs.find((candidate) => candidate.pair === key)
  return '| ' + label + ' | ' + variants
    .map((variant) => pair.measure(variant.tokens).toFixed(2) + ':1')
    .join(' | ') + ' |'
})

const summary = [
  '# Measured palette data',
  '',
  'Generated by measure-contrast.mjs from the shipped .paper-night token block and [palettes.json](./palettes.json).',
  '',
  'The calculation is the same WCAG sRGB relative-luminance formula used by the existing frontend contrast specs. Ratios are rounded to two decimals for presentation; assertions use unrounded values. The 3:1 boundary figure is characterized for comparison and is not treated as an acceptance gate.',
  '',
  '## Issue measures',
  '',
  header,
  rule,
  ...rows,
  '',
  'Only --faint changes outside the surface and boundary group, in Open folio and Cedar layers. That small lift keeps faint text at or above 4.5:1 on their brighter surfaces. --ink, the rest of the ink ladder, and every accent and status token remain exactly at the shipped values.',
  '',
  'The complete foreground-by-surface matrix, surface hierarchy measures, CTA base and hover checks, and error-tint pairing are in [contrast-matrix.csv](./contrast-matrix.csv).',
  ''
].join('\n')
writeFileSync(resolve(reportDir, 'contrast-summary.md'), summary)
console.log('Measured ' + pairs.length + ' pairs across ' + variants.length + ' palettes.')
