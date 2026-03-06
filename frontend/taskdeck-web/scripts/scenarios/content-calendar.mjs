import {
  applyStarterPack,
  enqueueAndApplyInstruction,
  getDemoConfig,
  isoDaysFromNow,
  summarizeBoardForAgent,
} from '../demo-lib.mjs'

export async function run({ api, config: cfg }) {
  const config = cfg || getDemoConfig()

  const board = await api.post('/boards', {
    body: {
      name: 'DEMO: Content Calendar Scenario',
      description: 'Seeded content pipeline: ideas -> drafting -> review -> scheduled.',
    },
  })

  await applyStarterPack(api, {
    boardId: board.id,
    starterPackId: 'board-blueprint-content-calendar',
    dryRun: false,
  })

  const columns = await api.get(`/boards/${board.id}/columns`)
  const labels = await api.get(`/boards/${board.id}/labels`)
  const byColumn = new Map((columns || []).map((column) => [column.name, column]))
  const byLabel = new Map((labels || []).map((label) => [label.name, label]))

  const ideas = byColumn.get('Ideas')
  const drafting = byColumn.get('Drafting')
  const review = byColumn.get('Review')
  const scheduled = byColumn.get('Scheduled')
  if (!ideas || !drafting || !review || !scheduled) {
    throw new Error('Starter pack did not create expected content columns.')
  }

  const needsDraft = byLabel.get('needs-draft')
  const needsReview = byLabel.get('needs-review')
  const publishWeek = byLabel.get('publish-week')
  if (!needsDraft || !needsReview || !publishWeek) {
    throw new Error('Starter pack did not create expected content labels.')
  }

  await api.post(`/boards/${board.id}/cards`, {
    body: {
      columnId: ideas.id,
      title: 'Blog: Why proposal-first automations are safer',
      description: 'Explain review/approve/execute flow. Compare to autopilot approaches.',
      labelIds: [needsDraft.id],
    },
  })

  await api.post(`/boards/${board.id}/cards`, {
    body: {
      columnId: drafting.id,
      title: 'Release notes draft: Capture Loop MVP',
      description: 'Summarize Inbox capture -> triage -> proposal -> apply. Include screenshots.',
      dueDate: isoDaysFromNow(3),
      labelIds: [needsDraft.id, publishWeek.id],
    },
  })

  const cardForReview = await api.post(`/boards/${board.id}/cards`, {
    body: {
      columnId: review.id,
      title: 'Design: Automations empty-state panel',
      description: '3 example prompts + explanation of Queue vs Proposals vs Chat.',
      labelIds: [needsReview.id],
    },
  })

  await api.post(`/boards/${board.id}/cards`, {
    body: {
      columnId: scheduled.id,
      title: 'Tweet thread: Taskdeck demo walkthrough',
      description: 'Short series showing boards -> inbox -> triage -> proposals -> execute.',
      dueDate: isoDaysFromNow(1),
      labelIds: [publishWeek.id],
    },
  })

  await api.post(`/boards/${board.id}/cards`, {
    body: {
      columnId: scheduled.id,
      title: 'Schedule: Starter packs for common workflows',
      description: 'Feature engineering sprint + support triage + content calendar in the next release wave.',
      labelIds: [publishWeek.id],
    },
  })

  const instruction = `move card ${cardForReview.id} to column "Scheduled"`
  await enqueueAndApplyInstruction(api, { boardId: board.id, instruction })

  const cards = await api.get(`/boards/${board.id}/cards`)
  const snapshot = summarizeBoardForAgent({ board, columns, cards })

  return {
    board: { id: board.id, name: board.name },
    links: {
      uiBoard: `${config.uiBaseUrl}/workspace/boards/${board.id}`,
    },
    snapshot,
  }
}
