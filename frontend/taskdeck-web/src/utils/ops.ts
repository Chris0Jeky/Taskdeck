import type { CommandRunStatusValue } from '../types/ops'

const commandRunStatusByIndex = ['Queued', 'Running', 'Completed', 'Failed', 'TimedOut', 'Cancelled'] as const

export function normalizeCommandRunStatus(value: CommandRunStatusValue): typeof commandRunStatusByIndex[number] {
  if (typeof value === 'number') {
    return commandRunStatusByIndex[value] ?? 'Failed'
  }

  const found = commandRunStatusByIndex.find(v => v.toLowerCase() === value.toLowerCase())
  return found ?? 'Failed'
}
