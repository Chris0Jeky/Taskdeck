import {
  applyStarterPack,
  enqueueAndApplyInstruction,
  getDemoConfig,
  summarizeBoardForAgent,
} from '../demo-lib.mjs'

function isoDaysFromNow(days) {
  const value = new Date()
  value.setDate(value.getDate() + days)
  return value.toISOString()
}

export async function run({ api, config: cfg }) {
  const config = cfg || getDemoConfig()

  const board = await api.post('/boards', {
    body: {
      name: 'DEMO: Engineering Sprint',
      description:
        'A realistic sprint board seeded for demos and testing. Includes labels, due dates, a blocked item, and a queue-driven automation.',
    },
  })

  await applyStarterPack(api, {
    boardId: board.id,
    starterPackId: 'board-blueprint-engineering-sprint',
    dryRun: false,
  })

  const columns = await api.get(`/boards/${board.id}/columns`)
  const labels = await api.get(`/boards/${board.id}/labels`)
  const byColumn = new Map((columns || []).map((column) => [column.name, column]))
  const byLabel = new Map((labels || []).map((label) => [label.name, label]))

  const backlog = byColumn.get('Backlog')
  const inProgress = byColumn.get('In Progress')
  const review = byColumn.get('Review')
  if (!backlog || !inProgress || !review) {
    throw new Error('Starter pack did not create expected columns (Backlog/In Progress/Review).')
  }

  const bug = byLabel.get('bug')
  const techDebt = byLabel.get('tech-debt')
  const priorityHigh = byLabel.get('priority-high')

  const card1 = await api.post(`/boards/${board.id}/cards`, {
    body: {
      columnId: backlog.id,
      title: 'Fix: login error state resets unexpectedly',
      description: 'Repro: failed login -> error toast -> retry with correct password -> form clears too early.',
      dueDate: isoDaysFromNow(2),
      labelIds: [bug?.id, priorityHigh?.id].filter(Boolean),
    },
  })

  const card2 = await api.post(`/boards/${board.id}/cards`, {
    body: {
      columnId: inProgress.id,
      title: 'Refactor: consolidate API error mapping',
      description: 'Unify API error payload parsing across views; standardize toast messaging.',
      dueDate: isoDaysFromNow(4),
      labelIds: [techDebt?.id].filter(Boolean),
    },
  })

  const card3 = await api.post(`/boards/${board.id}/cards`, {
    body: {
      columnId: review.id,
      title: 'Add: empty-state guidance for Automations',
      description: 'Help users understand Queue vs Proposals vs Chat. Show 3 copy-paste examples.',
      labelIds: [priorityHigh?.id].filter(Boolean),
    },
  })

  await api.patch(`/boards/${board.id}/cards/${card2.id}`, {
    body: {
      isBlocked: true,
      blockReason: 'Waiting on decision: should Queue composer require board selection?',
    },
  })

  await api.post(`/boards/${board.id}/cards/${card1.id}/comments`, {
    body: {
      content: 'If this regresses again, add an E2E test around login error handling.',
    },
  })

  await api.post(`/boards/${board.id}/cards/${card3.id}/comments`, {
    body: {
      content: 'Demo tip: open this card, then show Automations -> Proposals to highlight review/execute.',
    },
  })

  const instruction =
    'create card "Spike: simulate LLM-driven user" in column "Backlog" with description ' +
    '"Add an agent runner that creates/moves tasks like a real user."'
  await enqueueAndApplyInstruction(api, { boardId: board.id, instruction })

  const cards = await api.get(`/boards/${board.id}/cards`)
  const snapshot = summarizeBoardForAgent({ board, columns, cards })

  return {
    board: { id: board.id, name: board.name },
    links: {
      uiBoard: `${config.uiBaseUrl}/workspace/boards/${board.id}`,
      uiAutomations: `${config.uiBaseUrl}/workspace/automations/proposals`,
    },
    snapshot,
  }
}
