/** Stamped from frontend inputs at production build time; development has no fixed identity. */
declare const __TASKDECK_FRONTEND_BUILD__: string | null
export const frontendBuildIdentity: string | null = typeof __TASKDECK_FRONTEND_BUILD__ === 'undefined' ? null : __TASKDECK_FRONTEND_BUILD__
