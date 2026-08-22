/**
 * Review surface — Spanish. Owning SFCs and the full semantic contract are in
 * `../en/review.ts`; only translation notes live here.
 *
 * Glossary: board → tablero, proposal → propuesta, capture → captura,
 * to apply → aplicar, to file away → archivar, to defer → posponer.
 * "Nib", "Composer", "Paper", "diff", "Inbox" and "JSON" stay in English
 * (product coinages and technical loanwords, ADR-0054 §3).
 *
 * Contract reminders that bind THIS file:
 *   - `age.*` are one- or two-character suffixes glued to a number.
 *   - `status.*` / `statusInline.*` are RENDERED labels only; the backend wire
 *     values (`PendingReview`, `Applied`, …) are compared in code and are not
 *     translated anywhere.
 *   - Everything the server sends — proposal summaries, board names, provenance
 *     rows, backend validation messages — arrives through `{placeholders}` and
 *     stays in whatever language the server produced.
 */
export default {
  queueRail: {
    eyebrow: 'Cola · {awaiting} en espera · {stale} estancadas',
    filters: {
      label: 'Filtros de la cola',
    },
    filter: {
      all: 'Todas',
      mine: 'Mías',
      stale: 'Estancadas',
    },
    riskNote:
      'Orden de riesgo: bajo, medio, alto, crítico. El orden solo cambia la presentación; las acciones de revisión siguen siendo manuales.',
    fileAway: {
      cta: 'Archivar {count} cerradas',
      label: 'Archivar {count} propuestas cerradas',
    },
    empty: 'Nada en este filtro.',
    cadence: {
      heading: 'Esta semana',
      applyRateLabel: 'Tasa de aplicación',
      applyRateEmpty: 'Aún sin decisiones',
    },
  },

  queueItem: {
    noSummary: '(sin resumen)',
    confidence: 'conf {value}',
    reach: '{count} op | {count} op',
    who: {
      assistant: 'asistente',
      capture: 'captura',
    },
  },

  age: {
    seconds: '{value}s',
    minutes: '{value}m',
    hours: '{value}h',
    days: '{value}d',
  },

  cadence: {
    ariaLabel: 'Actividad del último {count} día | Actividad de los últimos {count} días',
  },

  recent: {
    heading: 'Aplicadas hace poco',
    empty: 'Hoy no se ha aplicado nada.',
    noSummary: '(aplicada)',
    age: 'hace {age}',
  },

  main: {
    tagstamp: 'PROPUESTA · DIFF',
    ledeFallback:
      'En espera de decisión. Revisa el cambio, la procedencia y los efectos secundarios antes de aplicar.',
    dial: {
      caption: 'CONF',
      above: 'Por encima de tu umbral de aplicación',
      below: 'Por debajo de tu umbral de aplicación',
      threshold: '(fijado {value} · Ajustes)',
    },
    approvedBanner: {
      title: 'Aprobada — todavía no aplicada al tablero.',
      body: 'Queda un paso: pulsa ⏎ (o “{action}”) para escribirla en el tablero. Hasta entonces no cambia nada.',
    },
    keyHint: {
      fileAway: 'PULSA ⌫ PARA ARCHIVAR',
      confirmApply: 'PULSA ⏎ PARA APLICAR AL TABLERO · ⌫ PARA RECHAZAR',
      approve: 'PULSA ⏎ PARA APROBAR · ⌫ PARA RECHAZAR',
    },
    footer: 'REVISIÓN · {serial} · LOCAL-FIRST · REGISTRO',
  },

  decisionRail: {
    toolbar: {
      decision: 'Acciones de decisión',
      filing: 'Acciones de archivo',
    },
    stamp: {
      decision: 'DECISIÓN',
      settled: 'CERRADA',
    },
    summary: {
      none: 'Nada que decidir ahora',
      operations:
        '{count} operación · revisión explícita · aplicación atómica | {count} operaciones · revisión explícita · aplicación atómica',
    },
    step: {
      approve: 'Paso 1 de 2 · aprobar no cambia el tablero',
      execute: 'Paso 2 de 2 · esto lo escribe en el tablero',
    },
    reject: 'Rechazar',
    requestEdit: 'Pedir cambios',
    defer: 'Posponer',
    apply: {
      approve: 'Aprobar',
      execute: 'Aplicar al tablero',
      approveLabel: 'Aprobar la propuesta — paso 1 de 2, todavía no cambia el tablero',
      executeLabel:
        'Aplicar al tablero — paso 2 de 2, escribe este cambio en el tablero',
    },
    fileAway: {
      label: 'Archivar',
      ariaLabel: 'Archivar la propuesta',
    },
  },

  change: {
    title: 'El cambio',
    subTitle: '{count} operación · {board} | {count} operaciones · {board}',
    beforeEyebrow: 'Antes · hoy',
    afterEyebrow: 'Después · al aplicar',
    fieldsHeading: 'Cambios por campo',
    tag: {
      new: '· nuevo',
      kept: '· sin cambios',
    },
    before: {
      titleFallback: 'Ninguna propuesta seleccionada',
      bodyFallback: 'Revisa {count} operaciones de la propuesta antes de aplicar.',
      meta: '{board} · {source}',
      sourceFallback: 'propuesta',
    },
    after: {
      noParameterPreview: 'No se ha facilitado una vista previa de los parámetros de esta operación.',
      noPreviewTitle: 'Sin vista previa de operaciones',
      noPreviewBody: 'La propuesta no incluía los detalles de las operaciones.',
    },
    fields: {
      operationsKey: 'operaciones',
      none: 'ninguna',
      notProvided: 'no facilitado',
    },
  },

  provenance: {
    title: 'Procedencia',
    sub: 'Qué se leyó · qué no · qué se dedujo',
    empty: 'Todavía no hay procedencia para esta propuesta.',
    footnote:
      'La procedencia refleja el actor detrás de esta propuesta — un extractor determinista sin conexión para las capturas, o el proveedor de IA que hayas configurado para la automatización por chat.',
    viewAll: 'Ver todas las fuentes leídas →',
  },

  provenanceDrawer: {
    ariaLabel: 'Detalles de procedencia',
    title: 'Procedencia',
    close: 'Cerrar el panel de procedencia',
    meta: {
      model: 'Modelo',
      confidence: 'Confianza',
      confidenceValue: '{value}%',
      latency: 'Latencia',
      latencyValue: '{value}ms',
      promptVersion: 'Versión del prompt',
    },
    weight: {
      primary: 'Fuentes primarias',
      contextual: 'Contextuales',
      inferred: 'Deducidas',
      excluded: 'Excluidas',
    },
    evidenceTitle: 'Enlaces de evidencia',
    evidenceSpan: 'caracteres {start}–{end}',
    viewTranscript: 'Ver en la transcripción',
    hideTranscript: 'Ocultar la transcripción',
    copyJson: 'Copiar JSON',
    copied: '¡Copiado!',
    copyFailed: 'No se pudo copiar',
    report: 'Informar de una sugerencia mala',
  },

  transcript: {
    title: 'En la transcripción',
    close: 'Cerrar',
    speaker: 'Hablante: {name}',
    loading: 'Cargando la transcripción…',
    unresolved: 'Este intervalo de evidencia ya no coincide con la transcripción guardada.',
    error: {
      notFound: 'Esta transcripción ya no está disponible.',
      unauthorized: 'No has iniciado sesión para ver esta transcripción.',
      generic: 'No se pudo cargar la transcripción. Inténtalo de nuevo.',
    },
  },

  sideEffects: {
    title: 'Efectos secundarios',
    sub: 'Qué llega · qué no · qué se archiva',
    empty: 'No hay efectos secundarios declarados.',
    riskEyebrow: 'Consideraciones al aplicar',
    fallback: {
      summary: 'Detalles del riesgo no disponibles',
      description: 'Revisa los efectos secundarios declarados antes de aplicar.',
    },
  },

  conflicts: {
    title: 'Conflictos y avisos',
    sub: {
      clear: 'Lo que ha notado el sistema · todo despejado',
      counted:
        'Lo que ha notado el sistema · {count} menor | Lo que ha notado el sistema · {count} elementos',
    },
    empty: 'Nada señalado.',
    tone: {
      warn: 'AVISO',
      ok: 'DESPEJADO',
      info: 'INFO',
    },
  },

  history: {
    title: 'Historial · esta tarjeta',
    sub: 'Cada paso desde su creación',
    empty: 'Sin historial registrado.',
    status: {
      pending: 'PENDIENTE',
      applied: 'APLICADA',
      past: 'pasado',
      unknown: 'DESCONOCIDO',
    },
  },

  author: {
    heading: 'Autor',
    breakdownHeading: 'Desglose de la confianza',
    nameFallback: 'Propuesta',
    name: '{actor} · propuesta de {source}',
    confidence: '{value} de confianza',
    actor: {
      assistant: 'Asistente',
      capture: 'Captura',
    },
    component: {
      operationSafety: 'Seguridad de las operaciones',
    },
  },

  whyNow: {
    heading: 'Por qué ahora',
    noProposal: 'Ninguna propuesta seleccionada.',
    fallback: 'Esta propuesta está en espera de revisión según la fuente capturada con ella.',
    tune: 'Ajustar las heurísticas →',
  },

  similarPast: {
    heading: 'Decisiones parecidas anteriores',
    empty: 'No hay decisiones anteriores comparables.',
    verdict: {
      applied: 'APLICADA',
      rejected: 'RECHAZADA',
    },
    rateLabel: 'Tasa de aplicación en parecidas:',
    rateValue: '{applied} de {total} ({percent}%)',
  },

  keys: {
    heading: 'Decide con el teclado',
    spaceKey: 'espacio',
    enter: {
      approve: 'Aprobar la propuesta · paso 1 de 2',
      execute: 'Aplicar al tablero · paso 2 de 2',
    },
    edit: 'Pedir cambios · abre el Composer',
    reject: 'Rechazar · con motivo opcional',
    defer: 'Posponer 1h',
    provenance: 'Mostrar u ocultar el panel de procedencia',
    preview: 'Vista previa del diff en el detalle de la tarjeta',
  },

  revisionEditor: {
    stamp: 'EDITAR ANTES DE APROBAR',
    jsonError: 'Escribe un JSON válido antes de guardar.',
    reasonLabel: 'Motivo del cambio',
    reasonPlaceholder: '¿Por qué estás editando esta propuesta?',
    cancel: 'Cancelar',
    save: 'Guardar revisión',
    badge: '{count} revisión | {count} revisiones',
  },

  technical: {
    summary: 'Detalles técnicos',
    copy: 'Copiar los detalles técnicos',
    copied: 'Copiado',
    ariaLabel: 'Detalles técnicos de la propuesta',
  },

  diff: {
    serial: '§ DIFF',
    title: 'Detalles de las operaciones',
    hint: 'Pulsa Espacio para ocultar',
    loading: 'Cargando el diff…',
    storedBanner: '{status} · solo lectura — muestra la vista previa guardada del envío original.',
    revised: {
      lead: 'Esta propuesta se',
      emphasis: 'revisó',
      storedTail:
        'después del envío — la vista previa guardada muestra las operaciones originales, no las revisadas.',
      fallbackTail:
        'después del envío — las operaciones registradas muestran el envío original, no el revisado.',
    },
    liveCaveat: {
      lead: 'Esta vista previa refleja tu última',
      emphasis: 'edición guardada',
      tail: '— las operaciones revisadas, no la propuesta original.',
    },
    invalid: {
      line: '{reason} — la aplicación rechazará esta propuesta.',
      noOperations: 'Esta propuesta no contiene operaciones que aplicar',
    },
    storedEmpty: 'No hay ninguna vista previa guardada para esta propuesta.',
    empty: 'No hay cambios que previsualizar para esta propuesta.',
    storedAriaLabel: 'Vista previa guardada de la propuesta',
    liveAriaLabel: 'Diff de las operaciones de la propuesta',
    recordedAriaLabel: 'Operaciones registradas de la propuesta',
  },

  applyDialog: {
    title: '¿Aplicar al tablero?',
    lede: 'Aprobada. Todavía no se ha escrito nada en el tablero — este es el paso que la aplica.',
    noSummary: 'Esta propuesta no tiene resumen.',
    revisionNote:
      'Esta propuesta se editó — se aplicará su última revisión guardada, no las operaciones originales.',
    contentsWillApply: 'Se aplicará el contenido aprobado de esta propuesta.',
    operationsWillApply: 'Se aplicará {count} operación. | Se aplicarán {count} operaciones.',
    cancel: 'Todavía no',
    confirm: 'Aplicar al tablero',
  },

  empty: {
    eyebrow: 'Cola · {count} en espera',
    title: 'Nada pendiente. Bien.',
    body: 'Cuando el asistente tenga algo que proponer aparecerá aquí para revisarlo.',
    loading: 'Cargando las propuestas…',
    filtered: {
      title: 'Sin resultados en {filter}.',
      body: 'Cambia de filtro para revisar propuestas que siguen en espera en otra parte de la cola.',
    },
  },

  summary: {
    pendingReview: {
      label: 'En espera de revisión',
      helper: 'Cambios en espera de una decisión explícita.',
    },
    readyToExecute: {
      label: 'Listas para ejecutar',
      helper: 'Propuestas aprobadas que ya pueden llegar a los tableros.',
    },
    captureLinked: {
      label: 'Vinculadas a una captura',
      helper: 'Elementos de revisión llegados por el ciclo del Inbox.',
    },
    applied: {
      label: 'Aplicadas',
      helper: 'Propuestas ya ejecutadas con éxito.',
    },
  },

  status: {
    pendingReview: 'En espera de revisión',
    approved: 'Aprobada',
    applied: 'Aplicada',
    rejected: 'Rechazada',
    failed: 'Fallida',
    expired: 'Caducada',
    dismissed: 'Archivada',
  },

  statusInline: {
    pendingReview: 'en espera de decisión',
    approved: 'aprobada',
    applied: 'aplicada',
    rejected: 'rechazada',
    failed: 'fallida',
    expired: 'caducada',
    dismissed: 'archivada',
  },

  headerMeta: '{time} · {status}',

  prompt: {
    rejectReasonRequired: 'Para este nivel de riesgo el motivo es obligatorio:',
    rejectReasonOptional: 'Motivo del rechazo (opcional):',
  },

  toast: {
    approved: 'Propuesta aprobada para aplicarla al tablero',
    approveFailed: 'No se pudo aprobar la propuesta',
    rejected: 'Propuesta rechazada',
    rejectFailed: 'No se pudo rechazar la propuesta',
    rejectReasonRequired:
      'El motivo del rechazo es obligatorio para propuestas de riesgo alto y crítico',
    snoozed: 'Pospuesta 1 hora — volverá a tu cola.',
    snoozeFailed: 'No se pudo posponer la propuesta',
    applied: 'Propuesta aplicada al tablero',
    applyFailed: 'No se pudo aplicar la propuesta al tablero',
    dismissed: 'Propuesta archivada',
    dismissedRefreshing: 'Propuesta retirada de la vista. Actualizando...',
    dismissFailed: 'No se pudo archivar la propuesta',
    nothingToClear: 'No hay propuestas terminadas que limpiar.',
    cleared: 'Limpiada {count} propuesta terminada. | Limpiadas {count} propuestas terminadas.',
    clearFailed: 'No se pudieron limpiar las propuestas',
    diffFailed: 'No se pudo cargar el diff de la propuesta',
    loadProposalFailed: 'No se pudo cargar la propuesta',
    loadProposalsFailed: 'No se pudieron cargar las propuestas',
    noLongerAvailable: 'Esta propuesta ya no está disponible para ti.',
    feedbackRecorded: 'Comentario registrado para esta sugerencia.',
    feedbackFailed: 'No se pudo registrar el comentario',
    noProposalToReport: 'No hay ninguna propuesta seleccionada que informar.',
    provenanceToggleUnwired:
      'El botón de procedencia todavía no está conectado; la procedencia se muestra aquí abajo.',
    revisionBusyFileAway: 'Guarda o cancela la revisión antes de archivar esta propuesta.',
    revisionBusyApply: 'Guarda o cancela la revisión antes de aplicar esta propuesta.',
    revisionBusyReject: 'Guarda o cancela la revisión antes de rechazar esta propuesta.',
    revisionBusyDefer: 'Guarda o cancela la revisión antes de posponer esta propuesta.',
    notDismissableYet: 'Esta propuesta sigue activa y todavía no se puede archivar.',
    bulkBusy: 'Espera a que termine la acción en curso antes de archivar más.',
    notApplyable:
      'Esta propuesta ya no se puede usar. Actualiza la revisión para ver el estado actual.',
    revisionStateUnknown:
      'El historial de revisiones no está disponible, así que esta propuesta no se puede verificar para aplicarla. Inténtalo de nuevo.',
    zeroOpApproved: 'Esta propuesta no contiene operaciones — aplicarla al tablero será rechazado.',
    zeroOpPending:
      'Esta propuesta no contiene operaciones que aplicar — la aplicación la rechazará. Recházala o archívala.',
    notRejectable:
      'Esta propuesta ya no se puede rechazar. Actualiza la revisión para ver el estado actual.',
    notEditable: 'Esta propuesta ya no se puede editar.',
    notDeferrable: 'Esta propuesta ya no se puede posponer.',
  },
}
