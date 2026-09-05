/**
 * Boards list surface — Spanish. Glossary as in `./home.ts`.
 *
 * `card.created` receives an already-formatted date; "Creado" agrees with
 * "tablero" (masculine).
 *
 * `error.timeout` and `error.cancelled` are the board store's boundary copy and
 * can appear on any board screen, not only this list — see `../en/boards.ts`.
 */
export default {
  eyebrow: 'Espacio de trabajo',
  title: 'Mis tableros',
  newBoard: '+ Nuevo tablero',
  create: {
    title: 'Crear un tablero nuevo',
    nameLabel: 'Nombre del tablero',
    namePlaceholder: 'Nombre del tablero',
    submit: 'Crear',
    cancel: 'Cancelar',
  },
  loading: 'Cargando tableros...',
  error: {
    retry: 'Volver a cargar los tableros',
    timeout:
      'La solicitud tardó demasiado y se detuvo. Comprueba la conexión y vuelve a intentarlo.',
    cancelled: 'La solicitud se detuvo antes de terminar. Vuelve a intentarlo.',
  },
  empty: {
    title: 'Sin tableros',
    hint: 'Empieza creando un tablero nuevo.',
    cta: '+ Crear tablero',
  },
  card: {
    openLabel: 'Abrir el tablero: {name}',
    noDescription: 'Sin descripción',
    created: 'Creado el {date}',
  },
}
