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
      olderErrors: '{count} ricevuta di errore precedente | {count} ricevute di errore precedenti',
      hideOlderErrors: 'Nascondi le ricevute di errore precedenti',
      earlierErrors: '{count} ricevuta di errore ancora precedente è uscita dall’archivio | {count} ricevute di errore ancora precedenti sono uscite dall’archivio',
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
  sidebar: {
    // Nomi parlati dei controlli di PaperSidebar (#3389). Glifi e badge
    // `· N` sono decorativi: ogni link/pulsante ha un `aria-label` esplicito.
    // `{label}` è l'etichetta visibile della voce — ancora inglese finché la
    // superficie sidebar non sarà estratta — quindi qui si localizza la
    // cornice, non il sostantivo.
    badge: {
      withCount: '{label}: {count} in attesa',
    },
    advanced: {
      label: 'Avanzate',
      show: 'Mostra',
      hide: 'Nascondi',
    },
    more: 'Altro',
    useAdvancedWorkspace: "Usa l'area di lavoro avanzata",
    theme: {
      switchToLight: 'Passa al tema Paper chiaro',
      switchToDark: 'Passa al tema Paper scuro',
    },
  },
}
