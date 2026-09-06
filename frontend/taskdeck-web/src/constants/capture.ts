/**
 * Client-side capture limits shared by every capture surface.
 *
 * These mirror the server contract (`CaptureRequestContract`) and exist so a
 * paste that the API would reject is refused beside the draft instead of
 * travelling the wire and coming back as a 400. The server stays the
 * authority; these are a courtesy guard, and they must not drift between the
 * Legacy modal and the Paper composer (GH-2141).
 */

/**
 * Longest transcript body accepted. Mirrors
 * `CaptureRequestContract.MaxTranscriptTextLength`. Source-specific on
 * purpose: quick (`Typed`) captures keep the backend's smaller general-text
 * limit, which the server enforces.
 */
export const MAX_TRANSCRIPT_LENGTH = 200_000

/**
 * UTF-8 transport guard for an uploaded transcript file: any valid
 * 200,000-code-unit transcript, including three-byte CJK text, plus the
 * optional three-byte UTF-8 BOM at the raw boundary.
 */
export const MAX_TRANSCRIPT_FILE_BYTES = 600_003
