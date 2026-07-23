import { useCallback, useEffect, useState } from 'react';
import type { MouseEvent } from 'react';
import { AssessmentModerationScreen } from '../screens/assessment-moderation/AssessmentModerationScreen';
import { AssessmentSelfScreen } from '../screens/assessment-self/AssessmentSelfScreen';
import { DashboardScreen } from '../screens/dashboard-bu/DashboardScreen';
import { RegisterDetailScreen } from '../screens/register-detail/RegisterDetailScreen';
import { RegisterListScreen } from '../screens/register-list/RegisterListScreen';
import { fetchMe, signOut, type MeResponse } from '../api/client';
import { strings } from '../strings';

type View = 'dashboard' | 'self' | 'moderation' | 'register' | 'register-detail';

interface NavEntry {
  label: string;
  /** Null for nav items that don't yet have a screen wired up — inert, no view switch. */
  view: View | null;
}

const navEntries: ReadonlyArray<NavEntry> = [
  { label: strings.nav.dashboard, view: 'dashboard' },
  { label: strings.nav.selfAssessment, view: 'self' },
  { label: strings.nav.managerReview, view: 'moderation' },
  { label: strings.nav.register, view: 'register' },
  { label: strings.nav.submissions, view: null },
  { label: strings.nav.admin, view: null },
];

/** Human-readable labels for the explicit `OrgRole` grants `GET /api/me` returns as raw C# enum
 * names (`Hap.Domain.Org.OrgRole`; see `LocalDevProvider.ExplicitRoleBySeedLabel` for the mirrored
 * server-side seed mapping). Hierarchy-derived roles (`HierarchyRoleResolver.ToRoleNames`) already
 * come back display-ready and need no mapping. */
const explicitRoleLabels: Record<string, string> = {
  PlatformAdmin: strings.shell.roleLabelPlatformAdmin,
  HigExecutive: strings.shell.roleLabelHigExecutive,
  BuDelegate: strings.shell.roleLabelBuDelegate,
  GroupViewer: strings.shell.roleLabelGroupViewer,
};

/** "Display name (role, role)" for the top bar; a person with no explicit or hierarchy-derived role
 * (a plain individual contributor) falls back to `individualRoleLabel` rather than showing nothing. */
function formatIdentity(me: MeResponse): string {
  const roles = [...me.explicitRoles.map((role) => explicitRoleLabels[role] ?? role), ...me.computedRoles];
  const roleText = roles.length > 0 ? roles.join(', ') : strings.shell.individualRoleLabel;
  return `${me.displayName} (${roleText})`;
}

interface AppShellProps {
  /** Called once sign-out completes (successfully or not — see handleSignOut below) so the caller
   * (App, FR-055's session gate) can return to the sign-in role-picker. AppShell itself unmounts when
   * that happens, which is what actually clears the fetched identity from memory — there is no
   * separate "clear" step here. */
  onSignedOut: () => void;
}

/**
 * The application frame per DESIGN.md A6: fixed deep-navy top bar, deep-navy left
 * nav, and a light content surface. A plain `useState` view switch (no router library — HAP-9)
 * lets the left-nav swap between the BU Dashboard (HAP-11), the Self-Assessment screen (HAP-8) and
 * the Manager Review screen (HAP-9); other feature screens remain inert nav entries until later
 * stories wire them up. Nav
 * items stay native `<a>` elements (role="link") with `aria-current="page"` on the active one — the
 * default-prevented click swaps the view without a real navigation. All chrome copy comes from the
 * externalised strings module (FR-067).
 *
 * The top bar also owns the caller's live identity (HAP-23; FR-055/FR-056): it fetches `GET /api/me`
 * itself on mount — independently of App's own session-gate fetch — so the shell has no dependency on
 * how its parent got here, and falls back to `identityUnavailable` if that fetch fails rather than
 * leaving stale or hard-coded copy on screen. The sign-out button calls the existing `signOut()`
 * (`POST /auth/signout`) and always hands control back to `onSignedOut`, even if the network call
 * itself fails — a best-effort local sign-out beats a stuck authenticated view.
 */
export function AppShell({ onSignedOut }: AppShellProps): JSX.Element {
  const [view, setView] = useState<View>('self');
  const [me, setMe] = useState<MeResponse | null>(null);
  // HAP-14: which initiative the register-detail screen shows — set when a register row is opened,
  // cleared implicitly by leaving the view (RegisterListScreen owns the list; this shell just tracks
  // the single selected id, same plain-useState pattern as `view` itself).
  const [selectedInitiativeId, setSelectedInitiativeId] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    fetchMe()
      .then((response) => {
        if (!cancelled) {
          setMe(response);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setMe(null);
        }
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const handleSignOut = useCallback((): void => {
    void signOut().finally(onSignedOut);
  }, [onSignedOut]);

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
        <div className="app-topbar-identity">
          <span className="app-user">
            <span className="app-user-label">{strings.shell.signedInAs}</span>{' '}
            <span className="app-user-role">{me ? formatIdentity(me) : strings.shell.identityUnavailable}</span>
          </span>
          <button type="button" className="app-signout" onClick={handleSignOut}>
            {strings.shell.signOut}
          </button>
        </div>
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
          {view === 'dashboard' && <DashboardScreen onStartSelfAssessment={() => setView('self')} />}
          {view === 'self' && <AssessmentSelfScreen />}
          {view === 'moderation' && <AssessmentModerationScreen />}
          {view === 'register' && (
            <RegisterListScreen
              onOpenInitiative={(id) => {
                setSelectedInitiativeId(id);
                setView('register-detail');
              }}
            />
          )}
          {view === 'register-detail' && selectedInitiativeId && (
            <RegisterDetailScreen initiativeId={selectedInitiativeId} onBack={() => setView('register')} />
          )}
        </main>
      </div>
    </div>
  );
}
