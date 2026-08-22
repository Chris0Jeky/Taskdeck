/**
 * Today surface — catálogo español.
 *
 * Contrato de honestidad del sellado (issue 1939): sellar solo escribe una hora
 * de sellado. No existe una acción para quitar el sello, así que ninguna cadena
 * de aquí puede prometer deshacerlo, ni decir que sellar archiva, bloquea,
 * oculta o borra algo.
 *
 * Estados vacíos: `notBuilt` = el panel aún no tiene consulta detrás;
 * `unavailable` = los datos son live pero no llegaron. No mezcles los dos.
 */
export default {
  seal: {
    action: 'Sellar el día',
    idleStatus: 'Sella cuando termines el día',
    autoStatus: 'Se sella solo en {duration}',
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
  },
  empty: {
    notBuiltTag: 'Todavía sin construir',
    stats: 'Los totales live de hoy no se pudieron cargar. Inbox y Revisión siguen siendo la fuente fiable.',
    cadence:
      'La cadencia no se pudo cargar. Son datos live, no una función que falte: no se deduce ningún patrón de trabajo.',
    ledgerSummary: 'Todavía sin registrar',
    ledger:
      'Taskdeck aún no registra un diario de eventos del día, así que este panel no tiene nada detrás y no se inventa ningún evento. Inbox y Revisión muestran lo que pasó de verdad hoy.',
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
