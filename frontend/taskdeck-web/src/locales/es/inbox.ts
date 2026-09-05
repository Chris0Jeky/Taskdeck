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
  // Se muestra EN LUGAR de `eyebrow` mientras se sustituye el ámbito (#2501):
  // los recuentos serían del ámbito que el usuario acaba de dejar. Sin plural:
  // no hay ningún número con el que concordar. Y sin ninguna palabra sobre la
  // carga: la tabla es la dueña del estado de carga, del error y del reintento.
  eyebrowUncounted: 'Inbox · superficie de captura',
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
  // del servidor. El texto NUNCA afirma qué motor produjo el resultado: uno de
  // los avisos del servidor (recuperación tras un fallo) declara él mismo que
  // la autoría es incierta (revisión de la PR #2224; #2212).
  degraded: {
    label: 'Clasificada sin una lectura del modelo confirmada',
    lead: 'Taskdeck no puede confirmar que el modelo haya producido este resultado. El servidor informó la clasificación así:',
    reason: 'Informado: {reason}',
    reviewProposal: 'Si el extractor determinista sin conexión produjo esta propuesta, es una conjetura basada en patrones de texto y no una lectura del modelo, y no incluye enlaces de evidencia. Léela con atención antes de aplicarla.',
    reviewTriaged: 'La clasificación terminó sin proponer nada. Puede que el extractor determinista sin conexión no reconociera ningún patrón, no que no haya nada que hacer: revisa tú la captura.',
    reviewConverted: 'Esta captura ya se aplicó a un tablero. Revisa los cambios resultantes frente al texto de la captura, porque el resultado podría no provenir de una lectura del modelo.',
    action: 'Si el modelo debía ejecutarse, revisa la configuración del proveedor de LLM.',
  },
  capture: {
    errorLead: 'Nota no guardada. Tu borrador sigue aquí.',
    errorDetail: 'Detalles: {reason}',
    // GH-2142 -- traducción automática (machine-translated).
    sessionExpiredReason: 'Tu sesión caducó antes de que se guardara esta captura.',
    draftRestoredLead: 'Borrador restaurado.',
    draftRestoredDetail:
      'Volver a iniciar sesión interrumpió esta captura, así que nada llegó al Inbox. Envíala cuando quieras.',
    draftRestoredTruncated: 'Parte de este borrador era demasiado largo, así que no se restauró por completo.',
    draftRestoredDiscard: 'Descartar este borrador',
    errorDiagnosticsLabel: 'Diagnóstico de la solicitud',
    errorFallback: 'Vuelve a intentarlo cuando la conexión esté disponible.',
    metadataCompatibilityLead: 'Captura guardada sin fecha ni etiquetas.',
    metadataCompatibilityDetail: 'Esta versión del servidor ignoró esos metadatos. No lo intentes de nuevo: la captura ya está en el Inbox.',
    source: {
      legend: 'Origen',
      typed: 'Nota escrita',
      transcript: 'Transcripción',
      transcriptNote:
        'Las capturas de transcripción se envían al asistente configurado para extraer tareas. Las notas escritas no.',
      tooLong: 'Esta transcripción es demasiado larga. La longitud máxima es de {max} caracteres.',
    },
  },
  // Etiquetas de los campos del Composer (#1871). "Texto" para `body`: es el
  // texto de la captura, igual que en `triage.edit.label`. "Fecha límite" es el
  // término del glosario para una fecha de vencimiento, como en
  // `triage.edit.metadata.dueDate`.
  //
  // Cada nombre accesible (`*Aria`) empieza por la etiqueta visible del campo y
  // después dice qué hace el control (WCAG 2.5.3, el patrón de la PR #2675). Si
  // se reescribe una etiqueta visible, hay que reescribir su nombre accesible:
  // `PaperCaptureComposer.spec.ts` comprueba la relación, no solo las cadenas.
  //
  // Los marcadores de posición siguen siendo marcadores: una pista sobre la
  // FORMA del valor, nunca el nombre del campo, que es lo que aportan la
  // etiqueta visible y el nombre accesible. `bodyPlaceholder` es una frase y va
  // en mayúscula inicial a propósito; `labelsPlaceholder` es un fragmento que
  // continúa el campo y va en minúscula.
  composer: {
    bodyLabel: 'Texto',
    bodyAria: 'Texto: escribe el contenido de esta captura',
    bodyPlaceholder: 'La idea, en lenguaje sencillo…',
    labelsLabel: 'Etiquetas',
    labelsAria: 'Etiquetas: escribe una etiqueta y pulsa Enter para añadirla',
    labelsPlaceholder: 'añade y pulsa Enter',
    dueLabel: 'Fecha límite (opcional)',
    dueAria: 'Fecha límite (opcional): elige cuándo vence esta captura',
    attachmentsUnavailable: 'Los archivos adjuntos aún no se guardan con las capturas.',
  },
  nib: {
    eyebrow: 'Captura rapida · {shortcut}',
    destinationWithBoard: 'Esta captura llega al Inbox vinculada a {board} para el triage.',
    destinationWithoutBoard: 'Esta captura llega al Inbox sin tablero para el triage.',
    selectedBoard: 'el tablero seleccionado',
    submit: 'Capturar',
  },
  // `boardAndColumn` se eliminó con #1984 (hallazgo 2): la lista del Inbox se
  // solicita por tablero y sin columna, así que nombrar una columna aquí
  // declaraba un filtro que nunca se aplicó.
  scope: {
    board: 'Tablero: {board}',
    clear: 'Mostrar todas las capturas',
  },
  empty: {
    scoped: 'No hay capturas en {scope}. Muestra todas las capturas para restaurar el Inbox completo.',
  },
  // Se añade a la línea del recuento durante una recarga en el MISMO ámbito,
  // con las filas todavía visibles y utilizables (#2501). En minúscula: va
  // detrás de un separador "·".
  refreshing: 'actualizando…',
  variantToggle: {
    label: 'Variante de captura',
  },
  variant: {
    nib: 'Nib',
    composer: 'Composer',
  },
  boardPicker: {
    // `label` encabeza los dos selectores de tablero. Los dos nombres
    // accesibles llevan la etiqueta visible delante (WCAG 2.5.3) y solo se
    // distinguen por lo que dicen después: "esta nueva captura" es la del
    // Composer, el borrador todavía sin enviar; "esta captura" es la de la fila
    // de triage, un elemento que ya está en el Inbox.
    label: 'Tablero',
    composerAria: 'Tablero: elige a dónde llegará esta nueva captura',
    triageAria: 'Tablero: elige a dónde va esta captura',
    noBoardOption: 'Sin tablero · llega al Inbox',
    selectPlaceholder: 'Selecciona un tablero…',
    viewOnlyOption: '{name} · solo lectura',
    viewOnlyHint: 'Los tableros de solo lectura necesitan acceso de escritura antes de poder clasificar nada en ellos.',
  },
  triage: {
    // Nombre de la región de la lista de capturas: dice qué contiene la
    // región, no cuáles capturas, así que no cambia en modo de solo lectura.
    tableAria: 'Elementos capturados',
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
    // Dónde queda una corrección sin guardar cuya captura sale de la lista
    // (#1999, punto 3): un cambio del filtro de tablero, una recarga que ya no
    // devuelve la fila, o el paso al historial de solo lectura. `{capture}` es
    // el extracto de la propia fila.
    //
    // `kept` y `discarded` son recibos de un momento. `held`, `blocked` y
    // `heldUneditable` son frases vigentes mientras se ven, así que cada una
    // termina diciendo qué puede hacer quien lee.
    //
    // `kept` dice "esta lista" a propósito: la corrección vive en la tabla
    // mientras la tabla exista, y prometerla tras recargar la página sería una
    // promesa que este mecanismo no puede cumplir.
    draft: {
      kept: 'La corrección sin guardar de “{capture}” no se ha perdido. Se conserva mientras sigas en esta lista de Inbox y vuelve con esa captura cuando reaparezca. No se guardó nada.',
      held: 'La corrección sin guardar de “{capture}” sigue conservada. Pulsa Editar captura en esa fila para recuperarla.',
      blocked: 'La corrección sin guardar de “{capture}” sigue conservada. Otra captura está abierta para editar: termina esa y luego pulsa Editar captura en esta fila para recuperarla.',
      heldUneditable: 'La corrección sin guardar de “{capture}” sigue conservada. Esta lista no edita una captura que está {status}, así que la corrección espera aquí hasta que esa captura vuelva a poder editarse.',
      restored: 'La corrección sin guardar de “{capture}” vuelve a estar en el editor, sobre la captura tal como está ahora. Guárdala o cancela como siempre.',
      discarded: 'La corrección sin guardar de “{capture}” se descartó: la captura ahora está {status} y su texto ya no se puede editar. No se guardó nada.',
      dismiss: 'Descartar estos avisos',
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
