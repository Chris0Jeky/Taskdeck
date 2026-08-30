/**
 * Shell chrome (barra superiore Paper) — italiano. Glossario come in `./home.ts`.
 *
 * "Account" resta in inglese: è la parola che gli utenti italiani usano per il
 * proprio profilo in un'applicazione, e "conto" leggerebbe come contabilità.
 *
 * `toast.label.*`: timbri di esito. Un timbro non concorda con un sostantivo
 * preciso — la stessa parola marca una cattura e una proposta — quindi i
 * participi restano al maschile impersonale ("Salvato", non "Salvata") e dove
 * possibile si usa una forma invariabile ("In coda", "Errore", "Avviso").
 */
export default {
  toast: {
    label: {
      saved: 'Salvato',
      queued: 'In coda',
      approved: 'Approvato',
      applied: 'Applicato',
      done: 'Fatto',
      noted: 'Nota',
      warning: 'Avviso',
      failed: 'Errore',
    },
    receipt: {
      showDetails: 'Mostra dettagli',
      hideDetails: 'Nascondi dettagli',
      copyDetails: 'Copia dettagli',
      copied: 'Copiato',
      copyFailed: 'Copia non riuscita',
      dismissNotification: 'Chiudi la notifica',
      errorDetails: 'Dettagli dell’errore: {message}',
    },
  },
  topbar: {
    notifications: 'Notifiche',
    appearance: 'Impostazioni aspetto',
    account: {
      trigger: 'Apri il menu account',
      label: 'Account',
      signedInAs: 'Accesso come {name}',
      profile: 'Profilo',
      appearance: 'Aspetto',
      signOut: 'Esci',
    },
  },
}
