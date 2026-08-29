/**
 * Inbox surface — Spanish. Glossary as in `./home.ts`.
 *
 * "Nib" and "Composer" are Taskdeck's own names for the two capture
 * affordances (ADR-0054 §3) and stay in English.
 *
 * `title.lead` opens the question, so it carries the opening "¿".
 *
 * `eyebrow` lleva DOS recuentos con etiquetas distintas (#1974): "por
 * clasificar" es la cola de verdad — la misma definición que el badge de la
 * barra lateral y que `home.status.awaitingTriage` — y "capturadas" es el
 * total, que nunca se presenta como cola.
 *
 * `eyebrow` es un mensaje plural elegido por `{total}`: en singular el
 * participio concuerda ("1 capturada", no "1 capturadas"). "Por clasificar" es
 * invariable y no necesita forma propia.
 */
export default {
  eyebrow:
    'Inbox · superficie de captura · {pending} por clasificar · {total} capturada | Inbox · superficie de captura · {pending} por clasificar · {total} capturadas',
  title: {
    lead: '¿Qué tienes en mente,',
    emphasis: 'en dos palabras?',
  },
  lede: 'Suelta la idea. Se queda aquí, intacta, hasta que tú la clasifiques. Nada llega al tablero sin tu aprobación.',
  history: {
    eyebrow: 'Archivo · historial de capturas · solo lectura',
    title: 'Historial de capturas archivadas',
    lede: 'Consulta las capturas conservadas para este tablero archivado. Restaura el tablero antes de crear, editar o clasificar trabajo.',
    tableTitle: 'Capturas archivadas',
    empty: 'No se encontraron capturas conservadas para este tablero archivado.',
    detail: {
      open: 'Mostrar la captura conservada completa',
      close: 'Ocultar la captura conservada completa',
      title: 'Captura conservada',
      loading: 'Cargando la captura conservada…',
      error: 'No se pudo cargar la captura conservada.',
      captured: 'Capturada',
      processed: 'Procesada',
      board: 'Tablero',
      triageRun: 'Ejecución de clasificación',
      promptVersion: 'Versión del prompt',
      proposalLink: 'Abrir el registro de la decisión',
      noProposal: 'De esta captura no se creó ningún registro de decisión.',
      none: 'No registrado',
    },
  },
  // Aviso de clasificación degradada (#2202). Es una advertencia sobre un
  // resultado correcto, no un fallo. `reason` reproduce literalmente el aviso
  // del servidor.
  degraded: {
    label: 'Clasificada sin el modelo',
    lead: 'El modelo no pudo responder, así que el extractor determinista sin conexión de Taskdeck clasificó esta captura en su lugar.',
    reason: 'Informado: {reason}',
    review: 'Un resultado determinista es una conjetura basada en patrones de texto, no una lectura del modelo, y no incluye enlaces de evidencia. Léelo con atención antes de aplicarlo.',
    action: 'Si el modelo debía ejecutarse, revisa la configuración del proveedor de LLM.',
  },
  capture: {
    errorLead: 'Nota no guardada. Tu borrador sigue aquí.',
    errorDetail: 'Detalles: {reason}',
    errorDiagnosticsLabel: 'Diagnóstico de la solicitud',
    errorFallback: 'Vuelve a intentarlo cuando la conexión esté disponible.',
    metadataCompatibilityLead: 'Captura guardada sin fecha ni etiquetas.',
    metadataCompatibilityDetail: 'Esta versión del servidor ignoró esos metadatos. No lo intentes de nuevo: la captura ya está en el Inbox.',
  },
  scope: {
    board: 'Tablero: {board}',
    boardAndColumn: 'Tablero: {board} · Columna: {column}',
    clear: 'Mostrar todas las capturas',
  },
  empty: {
    scoped: 'No hay capturas en {scope}. Muestra todas las capturas para restaurar el Inbox completo.',
  },
  variantToggle: {
    label: 'Variante de captura',
  },
  variant: {
    nib: 'Nib',
    composer: 'Composer',
  },
  boardPicker: {
    viewOnlyOption: '{name} · solo lectura',
    viewOnlyHint: 'Los tableros de solo lectura necesitan acceso de escritura antes de poder clasificar nada en ellos.',
  },
  triage: {
    boardPick: {
      loading: 'Cargando tableros…',
      loadFailed: 'No se pudieron cargar los tableros. Comprueba la conexión y vuelve a intentarlo.',
      retry: 'Volver a cargar los tableros',
      blocked: {
        noBoards: 'Todavía no hay tableros. Crea uno y esta captura podrá ir ahí.',
        noBoard: 'Elige primero un tablero. Pedir a la IA sigue desactivado hasta que selecciones uno.',
        viewOnly: 'Ese tablero es de solo lectura. Elige uno en el que puedas escribir.',
      },
    },
    decision: {
      sending: 'Enviando a Review…',
      keeping: 'Guardando para más tarde…',
      archiving: 'Archivando…',
      kept: 'Guardada para más tarde. Pide ayuda a la IA o archívala cuando quieras.',
      archived: 'Archivada. No se creó ninguna propuesta ni trabajo en el tablero.',
      nothingToPropose: 'La clasificación no encontró nada que proponer — a Review no llegó nada.',
      inReview: 'Enviada a Review — decide allí.',
      applied: 'Aplicada al tablero. Aquí no queda nada por hacer.',
      rejected: 'Rechazada. Esta captura no llegará a Review.',
      failed: 'El análisis falló, así que nada llegó a Review. Corrige el problema y vuelve a pedir ayuda a la IA.',
    },
    tag: {
      state: 'Estado: {label}. Dónde está ahora mismo esta captura.',
      source: 'Origen: {label}. Cómo llegó esta captura — no es un estado.',
    },
    // Corrección del texto antes de clasificar (GH-1951).
    //
    // `blocked.notEditable` enuncia el HECHO, no la causa: el servidor rechaza
    // la edición por varios motivos y nombrar uno solo sería una suposición
    // presentada como explicación. "Ask AI", "Keep" y "Archive" se quedan
    // en inglés porque son las etiquetas de los botones de esta superficie.
    edit: {
      action: 'Editar captura',
      label: 'Texto de la captura',
      placeholder: 'Corrige el texto capturado…',
      hint: 'Ajusta la redacción antes de que Ask AI la convierta en una propuesta. Guardar solo cambia la captura — desde aquí no llega nada a un tablero.',
      loading: 'Cargando el texto completo de la captura…',
      save: 'Guardar cambios',
      saving: 'Guardando…',
      cancel: 'Cancelar',
      close: 'Cerrar',
      retry: 'Reintentar',
      unknownReason: 'el servidor no dio ningún motivo',
      loadFailed: 'El texto completo de la captura no se cargó: {reason}',
      saveFailed: 'Los cambios de la captura no se guardaron: {reason}',
      decisionBlocked: 'Termina o cancela esta edición antes de pulsar Ask AI, Keep o Archive.',
      metadata: {
        legend: 'Fecha límite y etiquetas',
        dueDate: 'Fecha límite (opcional)',
        labels: 'Etiquetas (opcionales)',
        labelsPlaceholder: 'Escribe un nombre de etiqueta existente',
        addLabel: 'Añadir etiqueta',
        removeLabel: 'Quitar {label}',
        hint: 'Añade un nombre de etiqueta existente cada vez pulsando Enter. Quita una ficha para eliminarla; después guarda y pulsa Ask AI otra vez para reintentar la clasificación. Las comas siguen formando parte del nombre; aquí no se crean etiquetas.',
        unavailable: 'Esta API no devolvió metadatos editables. Guardar solo el texto conservará la fecha límite y las etiquetas almacenadas.',
      },
      blocked: {
        notEditable: 'El texto de esta captura no se puede editar. Pulsa Ask AI, Keep o Archive tal como está.',
        empty: 'El texto no puede estar vacío. Escribe algo, o cancela para dejar la captura como estaba.',
        unchanged: 'Todavía no ha cambiado nada. Edita el texto o los metadatos, o cancela para dejar la captura como estaba.',
        editorOpen: 'Otra captura está abierta para editar. Guarda o cancela esa edición: cambiar ahora descartaría el texto escrito allí.',
        busyElsewhere: 'Otra acción sobre una captura aún está terminando. Guardar vuelve en cuanto acabe.',
      },
    },
  },
}
