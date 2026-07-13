import { readFileSync } from 'node:fs'

const indexHtml = readFileSync('index.html', 'utf8')
const paperFonts = readFileSync('src/paper-fonts.css', 'utf8')
const viteConfig = readFileSync('vite.config.ts', 'utf8')
const favicon = readFileSync('public/favicon.svg', 'utf8')

describe('Paper branding assets', () => {
  it('bundles only the required Latin WOFF2 Paper font faces', () => {
    expect(paperFonts.match(/@font-face/g)).toHaveLength(11)
    expect(paperFonts.match(/\.woff2/g)).toHaveLength(11)
    expect(paperFonts).not.toMatch(/\.woff(?:['")])/)
    expect(paperFonts).not.toContain('fonts.googleapis.com')

    expect(paperFonts).toContain('fraunces-latin-300-normal.woff2')
    expect(paperFonts).toContain('fraunces-latin-400-italic.woff2')
    expect(paperFonts).toContain('fraunces-latin-500-italic.woff2')
    expect(paperFonts).toContain('inter-latin-600-normal.woff2')
    expect(paperFonts).toContain('jetbrains-mono-latin-600-normal.woff2')
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
