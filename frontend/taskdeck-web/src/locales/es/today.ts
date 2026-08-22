/**
 * Today surface — catálogo español.
 *
 * Contrato de honestidad del sellado (issue 1939): sellar solo escribe una hora
 * de sellado. No existe una acción para quitar el sello, así que ninguna cadena
 * de aquí puede prometer deshacerlo, ni decir que sellar archiva, bloquea,
 * oculta o borra algo.
 *
 * Estados vacíos: `notBuilt` = el panel aún no tiene consulta detrás;
 * `unavailable` = los datos son live y la petición falló; `loading` = la
 * petición sigue en curso (issue 1983). No mezcles los tres: decir "no se pudo
 * cargar" mientras la petición está en vuelo es la misma mentira.
 *
 * "Todavía sin construir" habla del PANEL, no de la base de datos (issue 1983):
 * los cambios en tableros y tarjetas sí se registran en el historial de
 * auditoría y se leen desde Actividad. Lo que falta es la consulta por día.
 */
export default {
  seal: {
    action: 'Sellar el día',
    idleStatus: 'Sella cuando termines el día',
    confirmTitle: '¿Sellar hoy? No se puede deshacer.',
    confirmEffect:
      'Sellar marca hoy con una hora de sellado y da el día por cerrado aquí. No archiva, bloquea, oculta ni borra nada: tus capturas, propuestas y tableros siguen funcionando igual.',
    confirmIrreversible: 'Taskdeck no tiene forma de quitar el sello, así que hoy queda sellado en cuanto confirmes.',
    confirmAction: 'Sellar el día',
    confirmCancel: 'Dejar el día abierto',
    sealingAction: 'Sellando…',
    sealedAction: 'Día sellado',
    sealedStatus: 'Sellado por hoy',
    sealedReason: 'Hoy está sellado. Taskdeck no tiene forma de quitar el sello, así que desde aquí no se reabre.',
    toastSealed: 'Día sellado. Hoy queda cerrado y el sello no se puede quitar.',
    toastFailed: 'No se pudo sellar el día. Inténtalo otra vez.',
  },
  note: {
    action: 'Escribe una nota',
    hint: 'Va a tu línea para mañana, aquí abajo.',
    sectionSub: 'Guardada con la fecha de hoy · la ves cuando vuelves a abrir Hoy',
    meta: 'guardada con la fecha de hoy',
    metaFailed: 'sin guardar · edítala de nuevo para reintentar',
  },
  loading: {
    cadence: 'Cargando la cadencia de hoy…',
    streak: 'Cargando tu racha…',
  },
  empty: {
    notBuiltTag: 'Todavía sin construir',
    stats: 'Los totales live de hoy no se pudieron cargar. Inbox y Revisión siguen siendo la fuente fiable.',
    cadence:
      'La cadencia no se pudo cargar. Son datos live, no una función que falte: no se deduce ningún patrón de trabajo.',
    ledgerSummary: 'Aún sin vista por día',
    ledger:
      'Taskdeck aún no tiene una consulta de diario por día, así que este panel no puede componerlo y no se inventa ningún evento. Tus cambios en tableros y tarjetas sí quedan registrados en el historial de auditoría: abre Actividad para leerlo y Revisión para las decisiones que hay detrás.',
    decisions:
      'Taskdeck aún no registra un diario de decisiones del día, así que este panel no tiene nada detrás. Abre Revisión para ver propuestas live y las decisiones que tomaste.',
    boards:
      'Taskdeck no registra qué tableros tocaste hoy, así que este panel no tiene nada detrás. Abre Tableros para ver el estado live.',
    carryOverNone: 'No hay tarjetas vencidas en el resumen live de hoy.',
    carryOverUnavailable: 'Los pendientes no se pudieron cargar. Abre Tableros para ver las tarjetas live.',
    streak:
      'Tu racha no se pudo cargar. Son datos live, no una función que falte: no se deduce ningún historial de actividad.',
  },
}
