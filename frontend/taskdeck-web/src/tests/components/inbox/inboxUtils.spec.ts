import { describe, expect, it } from 'vitest'

import { sourceLabel } from '../../../components/inbox/inboxUtils'

describe('inboxUtils', () => {
  it.each([
    ['MarkdownImport', 'Markdown'],
    ['WebClip', 'Web Clip'],
    ['ShareTarget', 'Share Target'],
    ['BrowserExtension', 'Browser Extension'],
    ['VsCodeExtension', 'VS Code'],
    [7, 'Markdown'],
    [8, 'Web Clip'],
    [9, 'Share Target'],
    [10, 'Browser Extension'],
    [11, 'VS Code'],
  ])('labels capture source %s', (source, expected) => {
    expect(sourceLabel(source)).toBe(expected)
  })
})
