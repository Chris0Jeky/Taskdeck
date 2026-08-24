import { existsSync, readFileSync, readdirSync } from 'node:fs'
import { resolve } from 'node:path'

function normalizeLineEndings(content: string): string {
  return content.replace(/\r\n/g, '\n')
}

const projectRoot = resolve(import.meta.dirname, '../../..')
const repoRoot = resolve(projectRoot, '../..')
const indexHtml = readFileSync(resolve(projectRoot, 'index.html'), 'utf8')
const mainTs = readFileSync(resolve(projectRoot, 'src/main.ts'), 'utf8')
const paperFonts = readFileSync(resolve(projectRoot, 'src/paper-fonts.css'), 'utf8')
const viteConfig = readFileSync(resolve(projectRoot, 'vite.config.ts'), 'utf8')
const favicon = readFileSync(resolve(projectRoot, 'public/favicon.svg'), 'utf8')
const pagesWorkflow = readFileSync(resolve(repoRoot, '.github/workflows/pages-frontend.yml'), 'utf8')
const packageJson = JSON.parse(readFileSync(resolve(projectRoot, 'package.json'), 'utf8'))
const packageLock = JSON.parse(readFileSync(resolve(projectRoot, 'package-lock.json'), 'utf8'))
const materialSymbolsRoot = resolve(projectRoot, 'node_modules/@material-symbols/font-400')
const materialSymbolsPackage = JSON.parse(
  readFileSync(resolve(materialSymbolsRoot, 'package.json'), 'utf8'),
)
const materialSymbolsCss = readFileSync(resolve(materialSymbolsRoot, 'outlined.css'), 'utf8')
const packageMaterialSymbolsLicense = normalizeLineEndings(
  readFileSync(resolve(materialSymbolsRoot, 'LICENSE'), 'utf8'),
)
const repoMaterialSymbolsLicense = normalizeLineEndings(
  readFileSync(
    resolve(repoRoot, 'LICENSES/Apache-2.0-material-symbols-font-400.txt'),
    'utf8',
  ),
)

function collectFiles(directory: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = resolve(directory, entry.name)
    return entry.isDirectory() ? collectFiles(path) : [path]
  })
}

const sourceRoot = resolve(projectRoot, 'src')
const runtimeSourceFiles = [
  resolve(projectRoot, 'index.html'),
  resolve(projectRoot, 'vite.config.ts'),
  resolve(projectRoot, 'tailwind.config.js'),
  resolve(projectRoot, 'package.json'),
  ...collectFiles(sourceRoot).filter((path) => !path.startsWith(resolve(sourceRoot, 'tests'))),
  ...collectFiles(resolve(projectRoot, 'public')),
].filter((path) => /\.(?:css|html|js|json|mjs|ts|vue)$/.test(path))
const activeFrontendSources = runtimeSourceFiles
  .map((path) => readFileSync(path, 'utf8'))
  .join('\n')

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
    expect(activeFrontendSources).not.toContain('family=Manrope')
    expect(activeFrontendSources).not.toContain('family=Space+Grotesk')
  })

  it('self-hosts the outlined Material Symbols face without font egress', () => {
    const materialSymbolsImports = Array.from(
      mainTs.matchAll(/import ['"]@material-symbols\/font-400\/([^'"]+)['"]/g),
      ([, stylesheet]) => stylesheet,
    )
    const materialSymbolsFaces = Array.from(
      materialSymbolsCss.matchAll(/url\("([^"?]+\.woff2)"\)/g),
      ([, face]) => face,
    )
    const ligatureCallSites = runtimeSourceFiles
      .filter((path) => path.endsWith('.vue'))
      .flatMap((path) =>
        Array.from(
          readFileSync(path, 'utf8').matchAll(
            /class="[^"]*\bmaterial-symbols-outlined\b[^"]*"/g,
          ),
        ),
      )

    expect(packageJson.dependencies['@material-symbols/font-400']).toBe('0.46.0')
    expect(packageLock.packages['node_modules/@material-symbols/font-400']).toMatchObject({
      version: '0.46.0',
      license: 'Apache-2.0',
    })
    expect(materialSymbolsPackage).toMatchObject({
      name: '@material-symbols/font-400',
      version: '0.46.0',
      license: 'Apache-2.0',
    })
    expect(materialSymbolsImports).toEqual(['outlined.css'])
    expect(materialSymbolsCss.match(/@font-face/g)).toHaveLength(1)
    expect(materialSymbolsFaces).toEqual(['./material-symbols-outlined.woff2'])
    expect(existsSync(resolve(materialSymbolsRoot, materialSymbolsFaces[0]!))).toBe(true)
    expect(ligatureCallSites).toHaveLength(10)
    expect(repoMaterialSymbolsLicense).toBe(packageMaterialSymbolsLicense)
    expect(repoMaterialSymbolsLicense).toContain('Apache License')
    expect(activeFrontendSources).not.toMatch(
      /https?:\/\/fonts\.(?:googleapis|gstatic)\.com/i,
    )
    expect(viteConfig).not.toContain('google-fonts-stylesheets')
  })

  it('ships the Material Symbols licence with the GitHub Pages artifact', () => {
    const licensePath = 'LICENSES/Apache-2.0-material-symbols-font-400.txt'
    const pagesLicensePath = `frontend/taskdeck-web/dist/${licensePath}`
    const pushTrigger = pagesWorkflow.slice(
      pagesWorkflow.indexOf('  push:'),
      pagesWorkflow.indexOf('  workflow_dispatch:'),
    )
    const licenseStepAt = pagesWorkflow.indexOf('- name: Include Material Symbols licence')
    const copyAt = pagesWorkflow.indexOf('cp "$source" "$destination"')
    const compareAt = pagesWorkflow.indexOf('cmp -s "$source" "$destination"')
    const configureAt = pagesWorkflow.indexOf('- name: Configure GitHub Pages')
    const uploadAt = pagesWorkflow.indexOf('- name: Upload Pages artifact')

    expect(pushTrigger).toContain(`      - ${licensePath}`)
    expect(pagesWorkflow).toContain(`source="${licensePath}"`)
    expect(pagesWorkflow).toContain(`destination="${pagesLicensePath}"`)
    expect(pagesWorkflow).toContain('mkdir -p "frontend/taskdeck-web/dist/LICENSES"')
    expect(pagesWorkflow).toContain('cp "$source" "$destination"')
    expect(pagesWorkflow).toContain('cmp -s "$source" "$destination"')
    expect(licenseStepAt).toBeGreaterThan(-1)
    expect(copyAt).toBeGreaterThan(licenseStepAt)
    expect(compareAt).toBeGreaterThan(copyAt)
    expect(configureAt).toBeGreaterThan(compareAt)
    expect(uploadAt).toBeGreaterThan(configureAt)
  })

  it('uses Paper branding for favicon and install metadata', () => {
    expect(indexHtml).toContain('<html lang="en">')
    expect(indexHtml).not.toContain('class="dark"')
    expect(indexHtml).toContain('href="/favicon.svg"')
    expect(indexHtml).not.toContain('/vite.svg')
    expect(indexHtml).toContain('<meta name="theme-color" content="#f3eee5" />')

    expect(viteConfig).toContain("theme_color: '#f3eee5'")
    expect(viteConfig).toContain("background_color: '#f3eee5'")
    expect(favicon).toContain('fill="#f3eee5"')
    expect(favicon).toContain('stroke="#a8421f"')
  })
})
