import { describe, expect, it } from 'vitest'

import { triageDegradedNotice } from '../../../components/inbox/inboxUtils'
import type { CaptureStatusValue } from '../../../types/capture'

/**
 * The predicate behind the degraded-triage notice (#2202).
 *
 * The rule under test is that a notice rides ONLY a completed triage run. The
 * cases that matter are the ones where `errorMessage` is present but the run
 * did not complete successfully — those are failures, and rendering them
 * through the degradation copy would be the same dishonesty pointing the other
 * way.
 */

const NOTICE =
  'LLM triage unavailable (ProviderDegraded); using deterministic extractor. Live provider request failed.'

describe('triageDegradedNotice', () => {
  it.each<[string, CaptureStatusValue]>([
    ['Triaged (completed, nothing to propose)', 'Triaged'],
    ['Triaged as the numeric 2', 2],
    ['ProposalCreated', 'ProposalCreated'],
    ['ProposalCreated as the numeric 3', 3],
    ['Converted', 'Converted'],
    ['Converted as the numeric 4', 4],
  ])('returns the notice verbatim for %s', (_label, status) => {
    expect(triageDegradedNotice({ status, errorMessage: NOTICE })).toBe(NOTICE)
  })

  it.each<[string, CaptureStatusValue]>([
    ['Failed', 'Failed'],
    ['Failed as the numeric 6', 6],
  ])('returns null for %s — that message is a real failure, not a degradation', (_label, status) => {
    expect(triageDegradedNotice({ status, errorMessage: 'Triage failed hard.' })).toBeNull()
  })

  /**
   * `LlmRequest.Cancel()` refuses only Processing and Completed requests, so a
   * FAILED capture can be cancelled — and `Cancel()` clears neither Status'
   * error text nor `ErrorMessage`. The row then surfaces as `Ignored` still
   * carrying its failure message. A `status !== Failed` rule would re-dress
   * that failure as a friendly degradation notice; the allowlist must not.
   */
  it.each<[string, CaptureStatusValue]>([
    ['Ignored', 'Ignored'],
    ['Ignored as the numeric 5', 5],
  ])('returns null for a failed-then-cancelled capture surfacing as %s', (_label, status) => {
    expect(triageDegradedNotice({ status, errorMessage: 'Triage failed hard.' })).toBeNull()
  })

  it.each<[string, CaptureStatusValue]>([
    ['New', 'New'],
    ['Triaging', 'Triaging'],
  ])('returns null for %s — the run has not completed', (_label, status) => {
    expect(triageDegradedNotice({ status, errorMessage: NOTICE })).toBeNull()
  })

  it('returns null for a clean completed capture', () => {
    expect(triageDegradedNotice({ status: 'ProposalCreated', errorMessage: null })).toBeNull()
    expect(triageDegradedNotice({ status: 'ProposalCreated' })).toBeNull()
  })

  it('treats a whitespace-only notice as no notice', () => {
    expect(triageDegradedNotice({ status: 'ProposalCreated', errorMessage: '   ' })).toBeNull()
  })

  it('trims surrounding whitespace but never rewrites the server text', () => {
    expect(triageDegradedNotice({ status: 'Triaged', errorMessage: `  ${NOTICE}  ` })).toBe(NOTICE)
  })

  it('returns null for a missing item or a missing status', () => {
    expect(triageDegradedNotice(null)).toBeNull()
    expect(triageDegradedNotice(undefined)).toBeNull()
    expect(triageDegradedNotice({ errorMessage: NOTICE })).toBeNull()
  })

  it('returns null for an out-of-contract status', () => {
    expect(triageDegradedNotice({ status: 99, errorMessage: NOTICE })).toBeNull()
  })
})
