/**
 * Today surface — catalogo italiano.
 *
 * Contratto di onestà del sigillo (issue 1939): sigillare scrive solo una data
 * di sigillo. Non esiste un'azione per togliere il sigillo, quindi nessuna
 * stringa qui può promettere un annullamento, né dire che sigillare archivia,
 * blocca, nasconde o cancella qualcosa.
 *
 * Stati vuoti: `notBuilt` = il pannello non ha ancora una query dietro;
 * `unavailable` = i dati sono live e la richiesta è fallita; `loading` = la
 * richiesta è ancora in corso (issue 1983). Non confondere i tre: dire "non si
 * è caricato" mentre la richiesta è in volo è la stessa bugia.
 *
 * "Non ancora fatto" parla del PANNELLO, non del database (issue 1983): le
 * modifiche a board e schede sono registrate nella cronologia di audit e si
 * leggono da Attività. A mancare è la query per giorno, non i dati.
 */
export default {
  seal: {
    action: 'Sigilla il giorno',
    idleStatus: 'Sigilla quando la giornata è finita',
    confirmTitle: 'Sigillare oggi? Non si può annullare.',
    confirmEffect:
      'Sigillare marca oggi con un orario di sigillo e segna la giornata come chiusa qui. Non archivia, blocca, nasconde né cancella nulla: le catture, le proposte e le board continuano a funzionare come prima.',
    confirmIrreversible: 'Taskdeck non ha un modo per togliere il sigillo: dopo la conferma oggi resta sigillato.',
    confirmAction: 'Sigilla il giorno',
    confirmCancel: 'Lascia il giorno aperto',
    sealingAction: 'Sigillo in corso…',
    sealedAction: 'Giorno sigillato',
    sealedStatus: 'Sigillato per oggi',
    sealedReason: 'Oggi è sigillato. Taskdeck non ha un modo per togliere il sigillo, quindi da qui non si riapre.',
    toastSealed: 'Giorno sigillato. Oggi è segnato come chiuso e il sigillo non si può togliere.',
    toastFailed: 'Non è stato possibile sigillare il giorno. Riprova.',
  },
  note: {
    action: 'Scrivi una nota',
    hint: 'Porta alla tua riga per domani, qui sotto.',
    sectionSub: 'Salvata con la data di oggi · la rivedi quando riapri Oggi',
    meta: 'salvata con la data di oggi',
    metaFailed: 'salvataggio non confermato · modificala di nuovo per riprovare',
  },
  loading: {
    cadence: 'Caricamento della cadenza di oggi…',
    streak: 'Caricamento della tua serie…',
  },
  empty: {
    notBuiltTag: 'Non ancora fatto',
    stats: 'I totali live di oggi non si sono caricati. Inbox e Revisione restano la fonte affidabile.',
    cadence:
      'La cadenza non si è caricata. Sono dati live, non una funzione mancante: nessun ritmo di lavoro viene dedotto.',
    ledgerSummary: 'Nessuna vista per giorno',
    ledger:
      'Oggi non è ancora collegato al registro attività, quindi questo pannello non può comporre un diario del giorno e nessun evento viene inventato. Le modifiche a board e schede restano registrate nella cronologia di audit: apri Attività per leggerla e Revisione per le decisioni che ci stanno dietro.',
    decisions:
      'Taskdeck non registra ancora un diario delle decisioni del giorno, quindi dietro questo pannello non c’è nulla. Apri Revisione per le proposte live e le decisioni che hai preso.',
    boards:
      'Taskdeck non registra quali board hai toccato oggi, quindi dietro questo pannello non c’è nulla. Apri Board per lo stato live.',
    carryOverNone: 'Nessuna scheda in ritardo nel riepilogo live di oggi.',
    carryOverUnavailable: 'Gli arretrati non si sono caricati. Apri Board per le schede live.',
    streak:
      'La tua serie non si è caricata. Sono dati live, non una funzione mancante: nessuno storico di attività viene dedotto.',
  },
}
