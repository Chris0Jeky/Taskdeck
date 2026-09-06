import { describe, expect, it } from 'vitest'
import { formatRecordedOperationActionLabel } from '../../utils/recordedOperationPresentation'

describe('formatRecordedOperationActionLabel', () => {
  it('formats known and unknown wire action names without changing their meaning', () => {
    expect(formatRecordedOperationActionLabel('CreateCard')).toBe('Create Card')
    expect(formatRecordedOperationActionLabel('unknown_action-type')).toBe('unknown action type')
  })

  it('preserves Unicode characters while normalizing separators', () => {
    expect(formatRecordedOperationActionLabel('réviser_carte')).toBe('réviser carte')
  })
})
