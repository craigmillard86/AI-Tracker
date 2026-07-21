/**
 * Externalised UI strings (FR-067). No user-facing copy is written as a literal
 * inside a component — the shell reads every visible string from here, and the
 * strings-guard test enforces it. A future i18n story swaps this module behind a
 * locale selector without touching component code.
 */
export const en = {
  appName: 'HIG AI Adoption Platform',
  appShortName: 'HAP',
  shell: {
    primaryNav: 'Primary',
    signedInAs: 'Signed in as',
    rolePlaceholder: 'Platform Admin',
    skipToContent: 'Skip to main content',
  },
  nav: {
    dashboard: 'Dashboard',
    assessments: 'Assessments',
    register: 'Initiative Register',
    submissions: 'Harris Submissions',
    admin: 'Admin',
  },
  home: {
    title: 'Welcome',
    body: 'This is the application shell. Feature screens are delivered in later stories.',
  },
} as const;
