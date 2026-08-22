/**
 * Shell chrome (Paper top bar) — English source catalog.
 *
 * Only the controls that carry an accessible name or visible label live here.
 * The breadcrumb text comes from `route.meta.breadcrumb` (route data, not copy)
 * and the workspace-mode option labels are still literal in the SFC — both are
 * separate extraction slices.
 *
 * `topbar.appearance` names the gear/sun icon-button by where it GOES
 * (the Appearance settings page), not by a generic "Settings": the glyph is a
 * ring with radiating spokes and reads as a sun, so a user pressing it is
 * looking for theme controls (#1932).
 */
export default {
  topbar: {
    notifications: 'Notifications',
    appearance: 'Appearance settings',
    account: {
      trigger: 'Open account menu',
      label: 'Account',
      signedInAs: 'Signed in as {name}',
      profile: 'Profile',
      appearance: 'Appearance',
      signOut: 'Sign out',
    },
  },
}
