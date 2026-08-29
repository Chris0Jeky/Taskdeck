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
    eyebrowScoped: 'Cola · {awaiting} en espera en este tablero · {stale} estancadas',
    liveAnnounce: '{count} propuesta en espera de revisión. | {count} propuestas en espera de revisión.',
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

  batchApprove: {
    selectLabel: 'Seleccionar {title} para aprobación por lotes',
    request: 'Revisar {count} aprobación seleccionada | Revisar {count} aprobaciones seleccionadas',
    requestLabel: 'Abrir la confirmación para {count} propuesta seleccionada | Abrir la confirmación para {count} propuestas seleccionadas',
    limitReached: 'Un lote puede contener como máximo {count} propuestas.',
    selectionChanged: 'La selección cambió porque una o más propuestas ya no son aptas. Revísala de nuevo.',
    receiptMismatch: 'Taskdeck no pudo confirmar el lote completo. Revisa la cola antes de volver a intentarlo.',
    approved: 'Se aprobó {count} propuesta; no se aplicó. | Se aprobaron {count} propuestas; no se aplicaron.',
    failed: 'No se pudieron aprobar las propuestas seleccionadas.',
    dialog: {
      title: '¿Aprobar las propuestas seleccionadas?',
      description: 'Confirmar la aprobación de {count} propuesta | Confirmar la aprobación de {count} propuestas',
      body: 'Taskdeck volverá a comprobar la {count} propuesta y aprobará el lote completo o ninguna. | Taskdeck volverá a comprobar las {count} propuestas y aprobará el lote completo o ninguna.',
      notApplied: 'Esto solo registra la aprobación. No se aplica nada a ningún tablero.',
      cancel: 'Seguir revisando',
      confirm: 'Aprobar {count} propuesta | Aprobar {count} propuestas',
    },
  },

  scope: {
    board: 'Tablero: {board}',
    clear: 'Mostrar todos los tableros',
  },

  historyMode: {
    notice: 'Historial de decisiones archivadas · solo lectura. Restaura el tablero antes de aprobar, rechazar, aplicar, editar, aplazar o archivar propuestas.',
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
    openLabel: 'Abrir propuesta aplicada: {title}',
  },

  appliedRecord: {
    ariaLabel: 'Registro de decisi\u00f3n de la propuesta aplicada',
    tagstamp: 'APLICADA \u00b7 SOLO LECTURA',
    eyebrow: 'Registro hist\u00f3rico',
    heading: 'Registro de decisi\u00f3n aplicada',
    lede:
      'Esta propuesta ya cambi\u00f3 el tablero. La decisi\u00f3n registrada y las operaciones efectivas son de solo lectura.',
    filingSummary: 'Registro hist\u00f3rico \u00b7 solo archivo',
    historicalNotice: 'Registro hist\u00f3rico aplicado. No hay m\u00e1s acciones de revisi\u00f3n disponibles.',
    field: {
      outcome: 'Resultado',
      decision: 'Decisi\u00f3n',
      decisionActor: 'Actor de la decisi\u00f3n',
      decisionTime: 'Hora de la decisi\u00f3n',
      appliedTime: 'Hora de aplicaci\u00f3n',
    },
    value: {
      applied: 'Aplicada',
      approved: 'Aprobada',
      notRecorded: 'No registrado',
    },
    operations: {
      heading: 'Operaciones aplicadas',
    },
  },

  main: {
    tagstamp: 'PROPUESTA · DIFF',
    ledeFallback:
      'En espera de decisión. Revisa el cambio, la procedencia y los efectos secundarios antes de aplicar.',
    dial: {
      modelCaption: 'MODELO',
      derivedCaption: 'DERIVADA',
      modelReported: 'Promedio declarado por elemento',
      derived: 'Promedio de verificación',
      deterministic: 'DETERMINISTA',
      notReported: 'NO DECLARADA',
      noModelNumber: 'Sin valor de confianza del modelo',
    },
    approvedBanner: {
      title: 'Aprobada — todavía no aplicada al tablero.',
      body: 'Queda un paso: pulsa ⏎ (o “{action}”) para escribirla en el tablero. Hasta entonces no cambia nada.',
    },
    decisionReceipt: {
      approved: {
        title: 'Aprobada — aún no se ha aplicado al tablero.',
        body: 'La revisión permanece aquí. Elige {action} cuando quieras cambiar el tablero.',
      },
      applied: {
        title: 'Aplicada al tablero.',
        body: 'Esta propuesta sigue disponible para inspección aquí; encuéntrala de nuevo en Aplicadas recientemente.',
      },
      rejected: {
        title: 'Rechazada.',
        body: 'Esta propuesta no se aplicó y sigue disponible para inspección aquí.',
      },
      deferred: {
        title: 'Pospuesta.',
        body: 'Esta propuesta volverá a Revisión cuando termine su aplazamiento.',
      },
    },
    keyHint: {
      fileAway: 'PULSA ⌫ PARA ARCHIVAR',
      confirmApply: 'PULSA ⏎ PARA APLICAR AL TABLERO',
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
    editLock: {
      editing: 'Estás editando esta propuesta abajo: las decisiones se reanudan cuando guardes o canceles la edición.',
      saving: 'Guardando tu edición: las decisiones se reanudan cuando termine.',
      cancel: 'Cancelar edición',
    },
  },

  change: {
    title: 'El cambio',
    subTitle: '{count} operación · {board} | {count} operaciones · {board}',
    beforeEyebrow: 'Antes · hoy',
    beforeEyebrowApplied: 'Antes · registrado',
    afterEyebrow: 'Después · al aplicar',
    afterEyebrowApplied: 'Después · aplicado',
    fieldsHeading: 'Cambios por campo',
    tag: {
      new: '· nuevo',
      kept: '· sin cambios',
    },
    before: {
      titleFallback: 'Ninguna propuesta seleccionada',
      bodyFallback: 'Revisa {count} operaciones de la propuesta antes de aplicar.',
      bodyApplied: 'Se registró {count} operación de la propuesta. | Se registraron {count} operaciones de la propuesta.',
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
    details: {
      show: 'Mostrar detalles de procedencia',
      hide: 'Ocultar detalles de procedencia',
    },
    footnote: {
      deterministic:
        'Procedencia registrada: {label} — esta propuesta la generó el extractor determinista sin conexión de Taskdeck.',
      mock: 'Procedencia registrada: {label} — esta propuesta la generó el proveedor simulado integrado de Taskdeck, no un modelo real.',
      provider:
        'Procedencia registrada: {label} — esta propuesta la generó el proveedor de IA que has configurado, por lo que su texto de origen se envió a ese proveedor.',
    },
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
    confidenceHeading: 'Fuente de confianza',
    modelReportedHeading: 'Confianza por elemento declarada por el modelo',
    details: {
      show: 'Mostrar detalles de confianza',
      hide: 'Ocultar detalles de confianza',
    },
    nameFallback: 'Propuesta',
    name: '{actor} · propuesta de {source}',
    modelConfidence: 'promedio declarado por el modelo {value}',
    derivedConfidence: 'promedio derivado {value}',
    deterministic: 'Extracción determinista · sin confianza del modelo',
    notReported: 'No se declaró confianza del modelo',
    actor: {
      assistant: 'Asistente',
      capture: 'Captura',
    },
  },

  whyNow: {
    heading: 'Por qué ahora',
    noProposal: 'Ninguna propuesta seleccionada.',
    fallback: 'Esta propuesta está en espera de revisión según la fuente capturada con ella.',
  },

  similarPast: {
    heading: 'Decisiones parecidas anteriores',
    empty: 'No hay decisiones anteriores comparables.',
    details: {
      show: 'Mostrar decisiones parecidas',
      hide: 'Ocultar decisiones parecidas',
    },
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
    regionLabel: 'Edita esta propuesta antes de aprobarla',
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

  rejectDialog: {
    title: '¿Rechazar esta propuesta?',
    lede: 'Al rechazarla la propuesta se cierra. En el tablero no cambia nada.',
    noSummary: 'Esta propuesta no tiene resumen.',
    reasonOptionalLabel: 'Motivo (opcional)',
    reasonRequiredLabel: 'Motivo (obligatorio)',
    reasonPlaceholder: '¿Por qué no sigue adelante?',
    requiredNote: 'Las propuestas de riesgo alto o crítico necesitan un motivo registrado.',
    cancel: 'Consérvala',
    confirm: 'Rechazar la propuesta',
  },

  empty: {
    eyebrow: 'Cola · {count} en espera',
    title: 'Nada pendiente. Bien.',
    body: 'Cuando el asistente tenga algo que proponer aparecerá aquí para revisarlo.',
    loading: 'Cargando las propuestas…',
    accessRevoked: {
      title: 'Esta cola de revisión ya no está disponible para ti.',
      body: 'Tu acceso a estos tableros cambió, así que la cola se vació y dejó de actualizarse. Vuelve a cargar la página o elige un tablero al que todavía tengas acceso.',
    },
    scoped: {
      title: 'No hay propuestas en {scope}.',
      body: 'Esta lista de revisión está limitada al tablero activo. Muestra todos los tableros para restaurar la cola completa.',
    },
    filtered: {
      title: 'Sin resultados en {filter}.',
      body: 'Cambia de filtro para revisar propuestas que siguen en espera en otra parte de la cola.',
    },
    // Machine-translated (see ADR-0054).
    settledElsewhere: {
      eyebrow: 'Propuesta seleccionada',
      title: 'Esta propuesta ya no esta pendiente.',
      body: 'Se decidio o se retiro en otro lugar mientras la revisabas. Aqui no se decidio nada y no se abrio ninguna otra propuesta en su lugar.',
      return: 'Volver a la cola',
    },
    unavailable: {
      eyebrow: 'Propuesta solicitada',
      title: 'Esta propuesta no esta disponible.',
      body: 'La propuesta {id} ya no esta disponible para revisar. Puede haberse aplicado, archivado o eliminado.',
      return: 'Volver a Revision',
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
