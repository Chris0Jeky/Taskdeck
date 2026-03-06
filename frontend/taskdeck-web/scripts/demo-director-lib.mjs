import fs from 'node:fs/promises'
import path from 'node:path'

const DIRECTOR_ARTIFACT_ENTRIES = [
  'README.md',
  'run-summary.json',
  'snapshot.json',
  'trace.ndjson',
  'logs',
  'screenshots',
  'playwright',
]

export async function resetDemoDirectorArtifacts(artifactDir) {
  const targets = DIRECTOR_ARTIFACT_ENTRIES.map((entry) => path.join(artifactDir, entry))
  await Promise.all(targets.map((targetPath) => fs.rm(targetPath, { recursive: true, force: true })))
}

export function resolveDemoDirectorRuntime({ webRoot, e2eDb, resetE2EDb = false, freshServers = false }) {
  const normalizedE2EDb = typeof e2eDb === 'string' ? e2eDb.trim() : ''
  if (resetE2EDb && normalizedE2EDb.length === 0) {
    throw new Error('--reset-e2e-db requires --e2e-db')
  }

  return {
    e2eDbPath: normalizedE2EDb.length > 0 ? path.resolve(webRoot, normalizedE2EDb) : null,
    forceFreshServers: freshServers || resetE2EDb,
  }
}

export async function resetDemoDirectorE2EDb(e2eDbPath) {
  if (!e2eDbPath) {
    return
  }

  await fs.rm(e2eDbPath, { force: true })
}

export { DIRECTOR_ARTIFACT_ENTRIES }
