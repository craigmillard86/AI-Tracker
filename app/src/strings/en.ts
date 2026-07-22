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
  signIn: {
    title: 'Sign in',
    subtitle: 'Local dev provider — choose a seeded role to continue. No password.',
    loading: 'Loading available users…',
    loadError: 'Could not load the sign-in list. Is the API running?',
    signInError: 'Sign-in failed. Please try again.',
    signInAsPrefix: 'Sign in as',
  },
  assessment: {
    breadcrumb: 'Maturity assessment',
    pageTitle: 'Self-assessment',
    loading: 'Loading your self-assessment…',
    loadError: 'Could not load your self-assessment. Is the API running?',
    noOpenCycleTitle: 'No open assessment cycle',
    noOpenCycleBody:
      'There is no assessment cycle open for you to complete right now. Check back when the next cycle opens.',
    purposeLimitation:
      'Purpose: development, not performance management. Pre-populated with your prior scores — a "no change" month takes seconds. Score honestly.',
    dimensionOfTotal: (index: number, total: number): string => `Dimension ${index} of ${total}`,
    levelSetLegend: (dimensionName: string): string => `Select a level for ${dimensionName}`,
    lastMonthLabel: 'Last month',
    lastMonthPill: 'Last month',
    toDoPill: 'To do',
    levelAbbrev: (level: number): string => `L${level}`,
    levelOptionLabel: (level: number, levelName: string): string => `L${level} — ${levelName}`,
    evidenceLabel: 'Evidence / comment (optional, strongly encouraged)',
    evidencePlaceholder: 'What have you seen this month? Links, examples, blockers…',
    evidenceHint: 'Evidence makes manager moderation meaningful and reduces divergence.',
    saveDraft: 'Save draft',
    submitForReview: 'Submit for review',
    submitHint:
        "Your submission is visible to your manager for review. You'll see their moderated scores and comments once complete.",
    progressLabel: 'Progress',
    progressOfTotal: (scored: number, total: number): string => `${scored} of ${total}`,
    progressAnnouncement: (scored: number, total: number): string =>
      `Dimension progress, ${scored} of ${total} scored`,
    progressHintIncomplete: (remaining: number): string =>
      `${remaining} dimension${remaining === 1 ? '' : 's'} still need a score before you can submit.`,
    progressHintComplete: 'All dimensions scored — ready to submit.',
    projectedFloorLabel: 'Projected result',
    floorLevelLabel: 'Floor level',
    floorHeldBy: (dimensionName: string, level: number): string => `held by ${dimensionName} (L${level})`,
    notYetScored: 'Not yet scored',
    saveDraftSuccess: 'Draft saved.',
    saveDraftError: 'Could not save your draft. Please try again.',
    saveDraftLocked: 'This assessment cycle is closed and no longer accepts changes.',
    saveDraftAlreadySubmitted: 'This assessment has already been submitted and can no longer be edited.',
    submitIncomplete: 'Score every dimension before submitting.',
    submitAlreadySubmitted: 'This assessment has already been submitted.',
    submitLocked: 'This assessment cycle is closed and no longer accepts submissions.',
    submitError: 'Could not submit your assessment. Please try again.',
    submittedTitle: 'Assessment submitted',
    submittedBody:
      "Thanks — your self-assessment has been submitted for review. You'll see your manager's moderated scores here once complete.",
    statusIncomplete: (scored: number, total: number): string => `Incomplete · ${scored} of ${total}`,
    statusComplete: 'Complete',
    readOnlyNotice:
      'This assessment cycle is closed. Your scores are shown for reference and can no longer be changed.',
  },
} as const;
