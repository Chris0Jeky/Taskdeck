import { describe, expect, it } from 'vitest'
import { buildInputAssistOptions, filterInputAssistOptions } from '../../utils/inputAssist'

describe('inputAssist utils', () => {
  it('builds unique options by normalized value and keeps first label/helper', () => {
    const options = buildInputAssistOptions([
      {
        value: 'health.check',
        label: 'Health Check',
        helperText: 'OpsAdmin role',
        keywords: ['health', 'status'],
      },
      {
        value: ' health.check ',
        label: 'Duplicate should be ignored',
        helperText: '',
        keywords: ['ping'],
      },
      {
        value: 'logs.query',
        label: 'Query Logs',
      },
    ])

    expect(options).toEqual([
      {
        value: 'health.check',
        label: 'Health Check',
        helperText: 'OpsAdmin role',
        keywords: ['health', 'status', 'ping'],
      },
      {
        value: 'logs.query',
        label: 'Query Logs',
        helperText: undefined,
        keywords: [],
      },
    ])
  })

  it('filters options by value, label, helper text, and keywords', () => {
    const options = buildInputAssistOptions([
      {
        value: 'health.check',
        label: 'Health Check',
        helperText: 'OpsAdmin role',
        keywords: ['status'],
      },
      {
        value: 'board-123',
        label: 'Release Board',
        helperText: 'board-123',
        keywords: ['release'],
      },
    ])

    expect(filterInputAssistOptions(options, 'opsadmin')).toHaveLength(1)
    expect(filterInputAssistOptions(options, 'release')[0]?.value).toBe('board-123')
    expect(filterInputAssistOptions(options, 'health')[0]?.value).toBe('health.check')
  })

  it('returns all options when query is empty or whitespace', () => {
    const options = buildInputAssistOptions([
      { value: 'a', label: 'A' },
      { value: 'b', label: 'B' },
    ])
    expect(filterInputAssistOptions(options, '')).toHaveLength(2)
    expect(filterInputAssistOptions(options, '   ')).toHaveLength(2)
  })

  it('skips seeds with empty or whitespace-only values', () => {
    const options = buildInputAssistOptions([
      { value: '', label: 'Empty' },
      { value: '  ', label: 'Whitespace' },
      { value: 'valid', label: 'Valid' },
    ])
    expect(options).toHaveLength(1)
    expect(options[0].value).toBe('valid')
  })

  it('merges helperText from later duplicate when first has none', () => {
    const options = buildInputAssistOptions([
      { value: 'cmd', label: 'Command' },
      { value: 'cmd', helperText: 'Added later' },
    ])
    expect(options[0].helperText).toBe('Added later')
  })

  it('uses value as label when label is not provided', () => {
    const options = buildInputAssistOptions([{ value: 'myCommand' }])
    expect(options[0].label).toBe('myCommand')
  })
})
