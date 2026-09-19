import { describe, expect, it } from 'vitest'
import enInbox from '../../locales/en/inbox'
import esInbox from '../../locales/es/inbox'
import itInbox from '../../locales/it/inbox'

/**
 * The inline picker appears only after Accept is asked to triage a board-less
 * capture. It chooses the board targeted by the resulting proposal; it does not
 * move the capture itself out of Inbox (#1871, PR #2700 review residual).
 */
describe('triage board-picker copy (#1871)', () => {
  it.each([
    [enInbox, 'Board: choose which board the triage proposal targets'],
    [esInbox, 'Tablero: elige el tablero al que se dirige la propuesta de triage'],
    [itInbox, 'Bacheca: scegli la bacheca a cui è destinata la proposta di triage'],
  ])('names proposal targeting without claiming the capture moves', (catalog, expected) => {
    expect(catalog.boardPicker.triageAria).toBe(expected)
  })

  it('keeps the composer and triage controls distinguishable', () => {
    expect(enInbox.boardPicker.triageAria).not.toBe(enInbox.boardPicker.composerAria)
    expect(esInbox.boardPicker.triageAria).not.toBe(esInbox.boardPicker.composerAria)
    expect(itInbox.boardPicker.triageAria).not.toBe(itInbox.boardPicker.composerAria)
  })
})
