/**
 * Board detail surface — Spanish. Glossary as in `./home.ts`, plus:
 *   card    → tarjeta (as in `./review.ts`)
 *   column  → columna
 *   archive → archivo / archivar
 *
 * "Archivar" is deliberately never rendered as "eliminar": the action hides the
 * board and is reversible, and the English says so too.
 */
export default {
  actions: {
    settings: 'Ajustes del tablero',
  },
  card: {
    add: '+ tarjeta',
    addAria: 'Añade una tarjeta a {column}',
    inputLabel: 'Título de la nueva tarjeta',
    placeholder: 'Título de la tarjeta',
    submit: 'Añadir',
    cancel: 'Cancelar',
    error: 'No se pudo añadir la tarjeta. Inténtalo de nuevo.',
    capture: '+ nota',
    captureAria: 'Toma una nota en Inbox para {column}',
  },
  column: {
    settings: 'Ajustes de la columna',
    settingsAria: 'Ajustes de la columna {column}',
    moveLeft: 'Mueve la columna a la izquierda',
    moveRight: 'Mueve la columna a la derecha',
    collapseAria: 'Contrae la columna {column}',
    expandAria: 'Expande la columna {column}',
    add: '+ columna',
    addAria: 'Añade una columna a este tablero',
    addInputLabel: 'Nombre de la nueva columna',
    addPlaceholder: 'Nombre de la columna',
    addSubmit: 'Añadir',
    addCancel: 'Cancelar',
    addError: 'No se pudo crear la columna. Inténtalo de nuevo.',
  },
  columnDialog: {
    eyebrow: 'Columna',
    title: 'Ajustes de la columna',
    close: 'Cierra los ajustes de la columna',
    nameLabel: 'Nombre de la columna',
    namePlaceholder: 'Por hacer',
    wipToggle: 'Pon un límite WIP',
    wipLabel: 'Máximo de tarjetas',
    wipHint:
      'Las tarjetas que pasen del límite marcan la cabecera de la columna. Déjalo apagado para no poner límite.',
    save: 'Guardar cambios',
    cancel: 'Cancelar',
    delete: 'Eliminar la columna',
    deleteBlocked: 'Primero mueve o elimina las tarjetas de esta columna.',
    deleteConfirm: '¿Eliminar "{name}" y sus ajustes? No se puede deshacer.',
    deleteConfirmAction: 'Sí, elimínala',
    deleteConfirmCancel: 'Consérvala',
    saveError: 'No se pudo guardar la columna. Inténtalo de nuevo.',
    deleteError: 'No se pudo eliminar la columna. Inténtalo de nuevo.',
  },
  boardDialog: {
    eyebrow: 'Tablero',
    title: 'Ajustes del tablero',
    close: 'Cierra los ajustes del tablero',
    nameLabel: 'Nombre del tablero',
    namePlaceholder: 'Mi tablero',
    descriptionLabel: 'Descripción',
    descriptionPlaceholder: '¿Para qué es este tablero?',
    save: 'Guardar cambios',
    cancel: 'Cancelar',
    lifecycle: 'Ciclo de vida',
    stateActive: 'Activo',
    stateArchived: 'Archivado',
    archiveHint:
      'Archivarlo lo esconde de las listas de tableros. No se elimina nada — puedes restaurarlo desde Espacio de trabajo → Archivo.',
    restoreHint:
      'Este tablero está archivado. Restáuralo para devolverlo a las listas de tableros activos.',
    archive: 'Mover al archivo',
    archiveConfirm: '¿Mover "{name}" al archivo? Podrás restaurarlo más tarde.',
    archiveConfirmHistory:
      'Las capturas y el historial de decisiones se conservan. Puedes consultarlos en Espacio de trabajo → Archivo; no aparecerán en Inbox ni en Revisión sin filtros mientras el tablero esté archivado.',
    archiveConfirmAction: 'Sí, archívalo',
    archiveConfirmCancel: 'Déjalo aquí',
    restore: 'Restaurar el tablero',
    saveError: 'No se pudo guardar el tablero. Inténtalo de nuevo.',
    archiveError: 'No se pudo archivar el tablero. Inténtalo de nuevo.',
    restoreError: 'No se pudo restaurar el tablero. Inténtalo de nuevo.',
  },
}
