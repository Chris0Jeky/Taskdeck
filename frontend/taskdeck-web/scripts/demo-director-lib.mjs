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

export { DIRECTOR_ARTIFACT_ENTRIES }
