import { describe, expect, it } from 'vitest'

import { captureRowState, sourceLabel } from '../../../components/inbox/inboxUtils'
import type { CaptureRowState } from '../../../components/inbox/inboxUtils'
import type { CaptureSourceValue, CaptureStatusValue } from '../../../types/capture'

describe('inboxUtils', () => {
  it.each<[CaptureSourceValue, string]>([
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

  // #1944 — the row must be able to say what a decision did. Both the string
  // and the numeric wire form are pinned: the API has shipped both.
  it.each<[CaptureStatusValue, CaptureRowState]>([
    ['New', 'undecided'],
    [0, 'undecided'],
    ['Triaging', 'sending'],
    [1, 'sending'],
    ['Triaged', 'nothingToPropose'],
    [2, 'nothingToPropose'],
    ['ProposalCreated', 'inReview'],
    [3, 'inReview'],
    ['Converted', 'applied'],
    [4, 'applied'],
    ['Ignored', 'rejected'],
    [5, 'rejected'],
    ['Failed', 'failed'],
    [6, 'failed'],
  ])('maps capture status %s to a row state', (status, expected) => {
    expect(captureRowState(status)).toBe(expected)
  })

  it('separates "triaged, nothing to propose" from "a proposal is waiting in Review"', () => {
    // Backend `CaptureStatusPolicy` (Domain/Enums/CaptureStatus.cs) maps a
    // COMPLETED triage to ProposalCreated when a proposal was linked and to
    // Triaged when none was. Collapsing the two into `inReview` sends the user
    // to Review to decide something that was never created — and Accept/Reject
    // are disabled on a Triaged row, so the instruction cannot be walked back.
    expect(captureRowState('Triaged')).not.toBe(captureRowState('ProposalCreated'))
    expect(captureRowState('ProposalCreated')).toBe('inReview')
  })

  it('never reports an unknown or absent status as undecided', () => {
    // An out-of-contract status is not a fresh capture; calling it `undecided`
    // would let a decided row render like an untouched one.
    expect(captureRowState(undefined)).toBe('unknown')
    expect(captureRowState('Quarantined' as unknown as CaptureStatusValue)).toBe('unknown')
    expect(captureRowState(99)).toBe('unknown')
  })

  it('marks exactly one state as still awaiting the user', () => {
    const states = (['New', 'Triaging', 'Triaged', 'ProposalCreated', 'Converted', 'Ignored', 'Failed'] as const)
      .map((status) => captureRowState(status))
      .filter((state) => state === 'undecided')
    expect(states).toEqual(['undecided'])
  })
})
