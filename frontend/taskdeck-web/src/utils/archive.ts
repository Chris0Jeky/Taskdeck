import type { RestoreStatusValue } from '../types/archive'

const restoreStatusByIndex = ['Available', 'Restored', 'Expired', 'Conflict'] as const

export function normalizeRestoreStatus(value: RestoreStatusValue): typeof restoreStatusByIndex[number] {
  if (typeof value === 'number') {
    return restoreStatusByIndex[value] ?? 'Available'
  }

  const found = restoreStatusByIndex.find(v => v.toLowerCase() === value.toLowerCase())
  return found ?? 'Available'
}
