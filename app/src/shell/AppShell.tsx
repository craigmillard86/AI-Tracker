import { AssessmentSelfScreen } from '../screens/assessment-self/AssessmentSelfScreen';
import { strings } from '../strings';

const navItems: ReadonlyArray<string> = [
  strings.nav.dashboard,
  strings.nav.assessments,
  strings.nav.register,
  strings.nav.submissions,
  strings.nav.admin,
];

/**
 * The application frame per DESIGN.md A6: fixed deep-navy top bar, deep-navy left
 * nav, and a light content surface. The self-assessment screen (HAP-8) is the
 * authenticated landing content; other feature screens mount into the content area
 * in later stories. All chrome copy comes from the externalised strings module
 * (FR-067).
 */
export function AppShell(): JSX.Element {
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
            {navItems.map((item) => (
              <li key={item}>
                <a className="app-nav-item" href="#main-content">
                  {item}
                </a>
              </li>
            ))}
          </ul>
        </nav>

        <main id="main-content" className="app-content">
          <AssessmentSelfScreen />
        </main>
      </div>
    </div>
  );
}
