import { workspaceInsightsApi } from '../api/workspaceInsights'

/** Private, user-requested export. The two authenticated reads are not an atomic snapshot. */
export async function collectWorkspaceMemoryExport(boardId: string, sessionIsCurrent: () => boolean) {
  if (!boardId || !sessionIsCurrent()) throw new Error('Sign in and select a board before exporting memory.')
  const [active, archived] = await Promise.all([
    workspaceInsightsApi.getMemories(boardId, false),
    workspaceInsightsApi.getMemories(boardId, true),
  ])
  if (!sessionIsCurrent()) throw new Error('Your session changed. Start the export again after signing in.')
  const records = [...active, ...archived]
  if (records.some(record => record.boardId !== boardId)) {
    throw new Error('The returned memories did not match this board. No file was downloaded.')
  }
  // A record can move between active and archived during the two reads. Keep
  // the latest returned revision once; retain all original fields and history.
  const memories = new Map<string, (typeof records)[number]>()
  for (const record of records) {
    const previous = memories.get(record.id)
    if (!previous || record.revision >= previous.revision) memories.set(record.id, record)
  }
  return JSON.stringify({
    format: 'taskdeck-private-board-memory',
    version: 1,
    boardId,
    exportedAt: new Date().toISOString(),
    scope: 'Current signed-in user; selected board; active and archived memories',
    notice: 'Contains private text and evidence. Two authenticated reads; not an atomic snapshot or a Taskdeck import format. Unsaved drafts are excluded.',
    memories: [...memories.values()],
  }, null, 2)
}

export function downloadWorkspaceMemoryJson(json: string, boardId: string) {
  const blob = new Blob([json], { type: 'application/json;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  try {
    link.href = url
    link.download = `taskdeck-private-memory-${boardId.replace(/[^a-zA-Z0-9-]/g, '_')}.json`
    document.body.appendChild(link)
    link.click()
  } finally {
    link.remove()
    URL.revokeObjectURL(url)
  }
}
