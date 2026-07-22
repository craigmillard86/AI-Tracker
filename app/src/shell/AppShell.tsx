import { useState } from 'react';
import type { MouseEvent } from 'react';
import { AssessmentModerationScreen } from '../screens/assessment-moderation/AssessmentModerationScreen';
import { AssessmentSelfScreen } from '../screens/assessment-self/AssessmentSelfScreen';
import { strings } from '../strings';

type View = 'self' | 'moderation';

interface NavEntry {
  label: string;
  /** Null for nav items that don't yet have a screen wired up — inert, no view switch. */
  view: View | null;
}

const navEntries: ReadonlyArray<NavEntry> = [
  { label: strings.nav.dashboard, view: null },
  { label: strings.nav.selfAssessment, view: 'self' },
  { label: strings.nav.managerReview, view: 'moderation' },
  { label: strings.nav.register, view: null },
  { label: strings.nav.submissions, view: null },
  { label: strings.nav.admin, view: null },
];

/**
 * The application frame per DESIGN.md A6: fixed deep-navy top bar, deep-navy left
 * nav, and a light content surface. A plain `useState` view switch (no router library — HAP-9)
 * lets the left-nav swap between the Self-Assessment screen (HAP-8) and the Manager Review screen
 * (HAP-9); other feature screens remain inert nav entries until later stories wire them up. Nav
 * items stay native `<a>` elements (role="link") with `aria-current="page"` on the active one — the
 * default-prevented click swaps the view without a real navigation. All chrome copy comes from the
 * externalised strings module (FR-067).
 */
export function AppShell(): JSX.Element {
  const [view, setView] = useState<View>('self');

  function handleNavClick(entry: NavEntry, event: MouseEvent<HTMLAnchorElement>): void {
    if (entry.view === null) {
      return;
    }
    event.preventDefault();
    setView(entry.view);
  }

  return (
    <div className="app-shell">
      <a className="app-skip-link" href="#main-content">
        {strings.shell.skipToContent}
      </a>

      <header className="app-topbar">
        <span className="app-brand">{strings.appName}</span>
        <span className="app-user">
          <span className="app-user-label">{strings.shell.signedInAs}</span>{' '}
          <span className="app-user-role">{strings.shell.rolePlaceholder}</span>
        </span>
      </header>

      <div className="app-body">
        <nav className="app-nav" aria-label={strings.shell.primaryNav}>
          <ul className="app-nav-list">
            {navEntries.map((entry) => (
              <li key={entry.label}>
                <a
                  className="app-nav-item"
                  href="#main-content"
                  aria-current={entry.view !== null && entry.view === view ? 'page' : undefined}
                  onClick={(event) => handleNavClick(entry, event)}
                >
                  {entry.label}
                </a>
              </li>
            ))}
          </ul>
        </nav>

        <main id="main-content" className="app-content">
          {view === 'self' ? <AssessmentSelfScreen /> : <AssessmentModerationScreen />}
        </main>
      </div>
    </div>
  );
}
