import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { axe } from 'vitest-axe';
import { AppShell } from '../shell/AppShell';
import { strings } from '../strings';

/** AppShell now mounts AssessmentSelfScreen (HAP-8) as its content — mock the fetch it issues on
 * mount. A 404 (no open cycle) keeps the rendered tree to shell chrome + strings-table literals
 * only, which is what these shell-level tests care about. AppShell also fetches its own `GET
 * /api/me` on mount (HAP-23) — a blanket 404 here exercises the "identity unavailable" fallback
 * path, since 404 (unlike 401) is not the fetchMe "not signed in" case and is treated as a failure. */
function installNoCycleFetchMock(): void {
  vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(new Response('', { status: 404 }))));
}

const meResponse = {
  personId: '00000000-0000-0000-0000-000000000001',
  externalRef: 'HAP-SEED-MGR',
  displayName: 'Grace Hopper',
  email: 'grace@synth.local',
  jobTitle: 'Engineering Manager',
  businessUnitCode: 'BU01',
  explicitRoles: [],
  computedRoles: ['Manager'],
  currentCycleStatus: null,
};

/** Routes by URL so identity (HAP-23) and the self-assessment fetch (HAP-8) can each answer
 * independently, and `/auth/signout` can be observed. */
function installSignedInFetchMock(onSignOut?: () => void): void {
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url === '/api/me') {
        return Promise.resolve(new Response(JSON.stringify(meResponse), { status: 200 }));
      }
      if (url === '/auth/signout' && init?.method === 'POST') {
        onSignOut?.();
        return Promise.resolve(new Response('', { status: 200 }));
      }
      // AssessmentSelfScreen's mount fetch — no open cycle, keeps this file focused on shell chrome.
      return Promise.resolve(new Response('', { status: 404 }));
    }),
  );
}

describe('AppShell', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders the brand, primary nav, and content landmark', async () => {
    installNoCycleFetchMock();

    render(<AppShell onSignedOut={() => {}} />);

    expect(screen.getByText(strings.appName)).toBeTruthy();
    expect(screen.getByRole('navigation', { name: strings.shell.primaryNav })).toBeTruthy();
    expect(screen.getByRole('main')).toBeTruthy();
    expect(screen.getByRole('heading', { level: 1, name: strings.assessment.pageTitle })).toBeTruthy();
    expect(await screen.findByText(strings.assessment.noOpenCycleTitle)).toBeTruthy();
    for (const item of Object.values(strings.nav)) {
      expect(screen.getByRole('link', { name: item })).toBeTruthy();
    }
  });

  it('has no detectable accessibility violations', async () => {
    installNoCycleFetchMock();

    const { container } = render(<AppShell onSignedOut={() => {}} />);
    await screen.findByText(strings.assessment.noOpenCycleTitle);

    // color-contrast needs a real canvas (unavailable in jsdom); it is exercised
    // against the running app by axe-driven checks in later UI stories.
    const results = await axe(container, {
      rules: { 'color-contrast': { enabled: false } },
    });
    expect(results.violations).toEqual([]);
  });

  it('shows the fallback identity text when GET /api/me is unavailable (HAP-23)', async () => {
    installNoCycleFetchMock();

    render(<AppShell onSignedOut={() => {}} />);

    expect(await screen.findByText(strings.shell.identityUnavailable)).toBeTruthy();
  });

  it('shows the actual signed-in identity — display name + role(s) — from GET /api/me (HAP-23)', async () => {
    installSignedInFetchMock();

    render(<AppShell onSignedOut={() => {}} />);

    expect(await screen.findByText('Grace Hopper (Manager)')).toBeTruthy();
    expect(screen.queryByText(strings.shell.identityUnavailable)).toBeNull();
  });

  it('clicking sign-out calls POST /auth/signout and then onSignedOut (HAP-23)', async () => {
    let signOutCalled = false;
    installSignedInFetchMock(() => {
      signOutCalled = true;
    });
    const onSignedOut = vi.fn();

    render(<AppShell onSignedOut={onSignedOut} />);
    await screen.findByText('Grace Hopper (Manager)');

    fireEvent.click(screen.getByRole('button', { name: strings.shell.signOut }));

    await waitFor(() => expect(onSignedOut).toHaveBeenCalledTimes(1));
    expect(signOutCalled).toBe(true);
  });

  it('sign-out works from the keyboard alone (HAP-23)', async () => {
    installSignedInFetchMock();
    const onSignedOut = vi.fn();

    render(<AppShell onSignedOut={onSignedOut} />);
    await screen.findByText('Grace Hopper (Manager)');

    const signOutButton = screen.getByRole('button', { name: strings.shell.signOut }) as HTMLButtonElement;
    signOutButton.focus();
    expect(document.activeElement).toBe(signOutButton);

    // jsdom does not synthesize a click from a native button's own Enter-key handling — fire both,
    // matching the pattern used for keyboard interaction elsewhere in this suite.
    fireEvent.keyDown(signOutButton, { key: 'Enter' });
    fireEvent.click(signOutButton);

    await waitFor(() => expect(onSignedOut).toHaveBeenCalledTimes(1));
  });

  it('the sign-out button has a visible focus outline (DESIGN.md A5)', async () => {
    installSignedInFetchMock();

    render(<AppShell onSignedOut={() => {}} />);
    await screen.findByText('Grace Hopper (Manager)');

    const signOutButton = screen.getByRole('button', { name: strings.shell.signOut });
    // The global `:focus-visible` rule (AppShell.css) is not evaluated by jsdom's computed styles —
    // this asserts the button is a plain, unstyled-away-from focusable native element that rule
    // applies to (no tabIndex=-1, no outline: none override on the element itself).
    expect(signOutButton.tabIndex).not.toBe(-1);
    expect(signOutButton.style.outline).toBe('');
  });

  // --- QA (HAP-23): the panel's own advisories, checked directly rather than taken on trust -----

  /** hap-code-reviewer advisory #4: the existing "keyboard alone" test fires BOTH a synthetic
   * keydown AND a synthetic click, so it cannot distinguish "Enter triggers sign-out" from "the
   * click handler works" — it would pass even if Enter did nothing. This test isolates the keydown:
   * no `@testing-library/user-event` is available in this project (checked — not a declared or
   * installed dependency, and adding one mid-QA would be a new-npm-dependency L2 trigger the
   * existing L1 panel never reviewed), and jsdom does not implement a native `<button>`'s built-in
   * behaviour of converting an Enter keypress into a click the way a real browser does. So a keydown
   * fired alone here is expected, in jsdom, to do nothing — confirming the prior test's keydown line
   * was inert scaffolding, not a real assertion. Real keyboard operability instead rests on the
   * button being an unadorned native `<button type="button">` with no keydown handler of its own to
   * pre-empt or mis-implement that native behaviour (asserted separately below) — that structural
   * fact is what actually keeps Enter/Space working in a real browser, not anything vitest can prove
   * under jsdom without a browser-mode runner. */
  it('QA: a keydown alone (no click) does not trigger sign-out under jsdom — the button has no keydown handler of its own to rely on native browser Enter-to-click behaviour', async () => {
    let signOutCalled = false;
    installSignedInFetchMock(() => {
      signOutCalled = true;
    });
    const onSignedOut = vi.fn();

    render(<AppShell onSignedOut={onSignedOut} />);
    await screen.findByText('Grace Hopper (Manager)');

    const signOutButton = screen.getByRole('button', { name: strings.shell.signOut });
    signOutButton.focus();
    fireEvent.keyDown(signOutButton, { key: 'Enter' });

    // Give any (incorrectly wired) async handler a turn before asserting nothing fired.
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(onSignedOut).not.toHaveBeenCalled();
    expect(signOutCalled).toBe(false);

    // The structural guarantee real keyboard operability actually depends on: no onkeydown
    // attribute/handler on the element that could intercept or preventDefault the native
    // Enter-to-click conversion a real browser performs on a focusable <button>.
    expect(signOutButton.onkeydown).toBeNull();
    expect(signOutButton.getAttribute('onkeydown')).toBeNull();
  });

  /** hap-code-reviewer advisory #1: `me` starts `null` and the render is `me ? … :
   * identityUnavailable`, so the very first render — before `GET /api/me` resolves — shows the same
   * "Identity unavailable" copy as a genuine fetch failure. This proves that flash is real (not
   * merely theoretical) by holding the `/api/me` response open and asserting the fallback text is
   * on screen while it's pending, then resolving it and asserting the real identity replaces it.
   * Verdict recorded in the story's QA section: this does not violate AC-1's "falls back gracefully
   * if /api/me is unavailable" literally — the copy is accurate at the instant it's shown (identity
   * genuinely isn't available *yet*) — but it is a real, user-visible defect against the AC's
   * *intent*: a normal successful sign-in should not flash an unavailability message. */
  it('QA: shows "Identity unavailable" while GET /api/me is still pending, not only on genuine failure — loading is indistinguishable from failure', async () => {
    let resolveMe!: (value: Response) => void;
    const mePromise = new Promise<Response>((resolve) => {
      resolveMe = resolve;
    });
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL) => {
        const url = typeof input === 'string' ? input : input.toString();
        if (url === '/api/me') {
          return mePromise;
        }
        return Promise.resolve(new Response('', { status: 404 }));
      }),
    );

    render(<AppShell onSignedOut={() => {}} />);

    // While /api/me is still pending, the fallback copy is on screen — identical to the
    // genuine-failure case, so a user cannot tell "loading" from "broken".
    expect(await screen.findByText(strings.shell.identityUnavailable)).toBeTruthy();

    resolveMe(new Response(JSON.stringify(meResponse), { status: 200 }));

    expect(await screen.findByText('Grace Hopper (Manager)')).toBeTruthy();
    expect(screen.queryByText(strings.shell.identityUnavailable)).toBeNull();
  });

  /** hap-code-reviewer advisory #3: `signOut()` never checks `response.ok`, and AppShell's
   * `.finally(onSignedOut)` runs regardless — so a non-2xx from `POST /auth/signout` still returns
   * the caller to the sign-in picker client-side. Story AC-3 requires the caller be unauthenticated
   * "until the user signs in again"; that server-side guarantee is unaffected by this diff (verified
   * separately against HAP-4's `Post_auth_signout_clears_the_session` — a real cookie round-trip,
   * not client wiring), and in the real `LocalDevProvider.SignOutAsync` a plain
   * `HttpContext.SignOutAsync` clearing a cookie has no failure mode that would return non-2xx in
   * this codebase, so this scenario doesn't arise in practice today. Documented as a pre-existing,
   * non-blocking robustness gap (not introduced by this story) rather than an AC violation: proves
   * the client still returns to the picker on a failed sign-out call, which is the client-visible
   * half of the tradeoff. */
  it('QA: sign-out failure path — a non-2xx POST /auth/signout still returns the caller to the picker client-side (documented best-effort tradeoff, not introduced by this story)', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
        const url = typeof input === 'string' ? input : input.toString();
        if (url === '/api/me') {
          return Promise.resolve(new Response(JSON.stringify(meResponse), { status: 200 }));
        }
        if (url === '/auth/signout' && init?.method === 'POST') {
          return Promise.resolve(new Response('', { status: 500 }));
        }
        return Promise.resolve(new Response('', { status: 404 }));
      }),
    );
    const onSignedOut = vi.fn();

    render(<AppShell onSignedOut={onSignedOut} />);
    await screen.findByText('Grace Hopper (Manager)');

    fireEvent.click(screen.getByRole('button', { name: strings.shell.signOut }));

    await waitFor(() => expect(onSignedOut).toHaveBeenCalledTimes(1));
  });
});
