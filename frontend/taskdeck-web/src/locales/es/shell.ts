/**
 * Shell chrome (barra superior de Paper) — español. Glosario como en `./home.ts`.
 *
 * "Cuenta" para account, y sesión para lo que se abre y se cierra: "cerrar
 * sesión" es lo que espera leer quien busca salir.
 *
 * `toast.label.*`: sellos de resultado. Un sello no concuerda con un sustantivo
 * concreto — la misma palabra marca una captura y una propuesta — así que los
 * participios van en masculino impersonal ("Guardado", no "Guardada") y donde
 * se puede se usa una forma invariable ("En cola", "Error", "Aviso").
 */
export default {
  toast: {
    label: {
      saved: 'Guardado',
      queued: 'En cola',
      approved: 'Aprobado',
      applied: 'Aplicado',
      done: 'Hecho',
      noted: 'Nota',
      warning: 'Aviso',
      failed: 'Error',
    },
    receipt: {
      showDetails: 'Mostrar detalles',
      hideDetails: 'Ocultar detalles',
      copyDetails: 'Copiar detalles',
      copied: 'Copiado',
      copyFailed: 'No se pudo copiar',
      dismissNotification: 'Cerrar la notificación',
      errorDetails: 'Detalles del error: {message}',
      olderErrors: '{count} recibo de error anterior | {count} recibos de error anteriores',
      hideOlderErrors: 'Ocultar recibos de error anteriores',
      earlierErrors: '{count} recibo de error anterior quedó fuera del archivo | {count} recibos de error anteriores quedaron fuera del archivo',
    },
  },
  topbar: {
    notifications: 'Notificaciones',
    appearance: 'Ajustes de apariencia',
    account: {
      trigger: 'Abrir el menú de cuenta',
      label: 'Cuenta',
      signedInAs: 'Sesión iniciada como {name}',
      profile: 'Perfil',
      appearance: 'Apariencia',
      signOut: 'Cerrar sesión',
    },
  },
  sidebar: {
    // Nombres hablados de los controles de PaperSidebar (#3389). Glifos e
    // insignia `· N` son decorativos: cada enlace/botón lleva un `aria-label`
    // explícito. `{label}` es la etiqueta visible del elemento — aún en inglés
    // hasta que se extraiga la superficie sidebar — así que aquí se localiza
    // el marco, no el sustantivo.
    badge: {
      withCount: '{label}: {count} en espera',
    },
    advanced: {
      show: 'Mostrar navegación avanzada',
      hide: 'Ocultar navegación avanzada',
    },
    more: 'Más',
    useAdvancedWorkspace: 'Usar el espacio de trabajo avanzado',
    theme: {
      switchToLight: 'Cambiar al tema Paper claro',
      switchToDark: 'Cambiar al tema Paper oscuro',
    },
  },
}
