import type { RegistrationAvailability, RegistrationMode } from '../types/auth'

const REGISTRATION_MODES: readonly RegistrationMode[] = ['Open', 'InviteOnly', 'Closed']

/**
 * Validate that an unknown provider payload is a well-formed
 * {@link RegistrationAvailability}. Auth surfaces MUST fail closed: any missing,
 * malformed, or older-shaped `registration` field is treated as "unknown" so the
 * UI never renders a dead registration form or link. The backend stays
 * authoritative; this guard only decides what is safe to render.
 */
export function isRegistrationAvailability(value: unknown): value is RegistrationAvailability {
  if (value === null || typeof value !== 'object') {
    return false
  }

  const candidate = value as Record<string, unknown>
  return (
    typeof candidate.mode === 'string' &&
    (REGISTRATION_MODES as readonly string[]).includes(candidate.mode) &&
    typeof candidate.isRegistrationAvailable === 'boolean' &&
    typeof candidate.inviteRequired === 'boolean'
  )
}

/**
 * Normalize a raw provider payload's `registration` field into a validated
 * {@link RegistrationAvailability} or `null` when the value is absent/malformed.
 * `null` means "availability unknown" and every caller MUST treat it as
 * registration being unavailable (fail closed).
 */
export function normalizeRegistrationAvailability(value: unknown): RegistrationAvailability | null {
  return isRegistrationAvailability(value) ? value : null
}
