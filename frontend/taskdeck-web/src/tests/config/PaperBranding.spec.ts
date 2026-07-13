import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const indexHtml = readFileSync(resolve(import.meta.dirname, '../../../index.html'), 'utf8')
const paperFonts = readFileSync(resolve(import.meta.dirname, '../../paper-fonts.css'), 'utf8')
const viteConfig = readFileSync(resolve(import.meta.dirname, '../../../vite.config.ts'), 'utf8')
const favicon = readFileSync(resolve(import.meta.dirname, '../../../public/favicon.svg'), 'utf8')

describe('Paper branding assets', () => {
  it('bundles only the required Latin WOFF2 Paper font faces', () => {
    const bundledFaces = Array.from(
      paperFonts.matchAll(/url\('@fontsource\/([^']+\.woff2)'\)/g),
      ([, face]) => face,
    )

    expect(paperFonts.match(/@font-face/g)).toHaveLength(11)
    expect(bundledFaces).toEqual([
      'fraunces/files/fraunces-latin-300-normal.woff2',
      'fraunces/files/fraunces-latin-400-normal.woff2',
      'fraunces/files/fraunces-latin-400-italic.woff2',
      'fraunces/files/fraunces-latin-500-normal.woff2',
      'fraunces/files/fraunces-latin-500-italic.woff2',
      'inter/files/inter-latin-400-normal.woff2',
      'inter/files/inter-latin-500-normal.woff2',
      'inter/files/inter-latin-600-normal.woff2',
      'jetbrains-mono/files/jetbrains-mono-latin-400-normal.woff2',
      'jetbrains-mono/files/jetbrains-mono-latin-500-normal.woff2',
      'jetbrains-mono/files/jetbrains-mono-latin-600-normal.woff2',
    ])
    expect(paperFonts).not.toMatch(/\.woff(?:['")])/)
    expect(paperFonts).not.toContain('fonts.googleapis.com')
  })

  it('uses Paper branding for favicon and install metadata', () => {
    expect(indexHtml).toContain('<html lang="en">')
    expect(indexHtml).not.toContain('class="dark"')
    expect(indexHtml).toContain('href="/favicon.svg"')
    expect(indexHtml).not.toContain('/vite.svg')
    expect(indexHtml).toContain('<meta name="theme-color" content="#f3eee5" />')
    expect(indexHtml).toContain('family=Manrope')
    expect(indexHtml).toContain('family=Space+Grotesk')
    expect(indexHtml).toContain('family=Material+Symbols+Outlined')

    expect(viteConfig).toContain("theme_color: '#f3eee5'")
    expect(viteConfig).toContain("background_color: '#f3eee5'")
    expect(favicon).toContain('fill="#f3eee5"')
    expect(favicon).toContain('stroke="#a8421f"')
  })
})
