import {
  applyStarterPack,
  approveAndExecuteProposal,
  getDemoConfig,
  summarizeBoardForAgent,
  waitFor,
} from '../demo-lib.mjs'

export async function run({ api, config: cfg }) {
  const config = cfg || getDemoConfig()

  const board = await api.post('/boards', {
    body: {
      name: 'DEMO: Support Triage',
      description:
        'Seeded support workflow + Inbox triage. Includes ignored item, triaged+applied item, and triaged+pending item.',
    },
  })

  await applyStarterPack(api, {
    boardId: board.id,
    starterPackId: 'board-blueprint-support-triage',
    dryRun: false,
  })

  const ignored = await api.post('/capture/items', {
    body: {
      boardId: board.id,
      source: 'Typed',
      text: 'Spam / duplicate ticket (demo): ignore this.',
    },
  })
  await api.post(`/capture/items/${ignored.id}/ignore`)

  const applied = await api.post('/capture/items', {
    body: {
      boardId: board.id,
      source: 'Typed',
      text:
        'Customer: Checkout fails with "Payment method not supported" on mobile.\n' +
        'Severity: high.\n' +
        'Need: reproduce + hotfix + notify support.',
    },
  })
  await api.post(`/capture/items/${applied.id}/triage`)

  const pending = await api.post('/capture/items', {
    body: {
      boardId: board.id,
      source: 'Typed',
      text:
        'Customer: Wants invoice PDF resend for last month.\n' +
        'Severity: low.\n' +
        'Need: verify account + resend invoice.',
    },
  })
  await api.post(`/capture/items/${pending.id}/triage`)

  const appliedItem = await waitFor(
    async () => {
      const item = await api.get(`/capture/items/${applied.id}`)
      return item?.provenance?.proposalId ? item : null
    },
    { label: 'triage(applied) -> proposalId', timeoutMs: 60_000, intervalMs: 900 },
  )

  await approveAndExecuteProposal(api, appliedItem.provenance.proposalId)

  await waitFor(
    async () => {
      const item = await api.get(`/capture/items/${pending.id}`)
      return item?.provenance?.proposalId ? item : null
    },
    { label: 'triage(pending) -> proposalId', timeoutMs: 60_000, intervalMs: 900 },
  )

  const columns = await api.get(`/boards/${board.id}/columns`)
  const cards = await api.get(`/boards/${board.id}/cards`)
  const snapshot = summarizeBoardForAgent({ board, columns, cards })

  return {
    board: { id: board.id, name: board.name },
    links: {
      uiBoard: `${config.uiBaseUrl}/workspace/boards/${board.id}`,
      uiInbox: `${config.uiBaseUrl}/workspace/inbox`,
      uiProposals: `${config.uiBaseUrl}/workspace/automations/proposals`,
    },
    captureItemIds: { ignored: ignored.id, applied: applied.id, pending: pending.id },
    snapshot,
  }
}
