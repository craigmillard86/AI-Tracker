import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import { strings } from '../strings';

/** Routes by URL so the authenticated path can answer both `/api/me` (session) and
 * `/api/me/assessment` (AssessmentSelfScreen, mounted inside AppShell as of HAP-8). */
function installMeFetchMock(meStatus: number, meBody: unknown): void {
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url === '/api/me') {
        return Promise.resolve(
          new Response(meStatus === 204 ? null : JSON.stringify(meBody), { status: meStatus }),
        );
      }
      if (url === '/api/me/assessment') {
        // No open cycle — keeps the authenticated-path assertion focused on session gating.
        return Promise.resolve(new Response('', { status: 404 }));
      }
      return Promise.reject(new Error(`unexpected fetch ${url}`));
    }),
  );
}

const meResponse = {
  personId: '00000000-0000-0000-0000-000000000001',
  externalRef: 'HAP-SEED-IND',
  displayName: 'Ada Lovelace',
  email: 'ada@synth.local',
  jobTitle: 'Software Engineer',
  businessUnitCode: 'BU01',
  explicitRoles: [],
  computedRoles: [],
  currentCycleStatus: null,
};

describe('App session gate (FR-055)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders the sign-in screen for an unauthenticated session', async () => {
    installMeFetchMock(401, null);

    render(<App />);

    expect(await screen.findByText(strings.signIn.title)).toBeTruthy();
  });

  it('renders the application shell for an authenticated session', async () => {
    installMeFetchMock(200, meResponse);

    render(<App />);

    expect(await screen.findByText(strings.appName)).toBeTruthy();
  });

  /** QA (HAP-23): AC-2's actual claim is an App-level round trip — sign out returns the app to the
   * sign-in role-picker with no stale authenticated view left behind. Every existing test exercises
   * either AppShell alone (mocking `onSignedOut`, never asserting what the real callback — flipping
   * App's own session state — produces) or App's initial gating in isolation. Nothing before this
   * rendered App, signed out, and checked the shell was actually replaced by the picker. Uses the
   * same fetch mock App already relies on to re-run its `checkSession` (`GET /api/me`) after
   * sign-out — post-signout that must resolve unauthenticated for the picker to render as the
   * genuine outcome of the state flip, not just a click handler firing. */
  it('signing out from the shell returns the app to the sign-in role-picker, with the shell gone (HAP-23, AC-2 round trip)', async () => {
    let signedOut = false;
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
        const url = typeof input === 'string' ? input : input.toString();
        if (url === '/api/me') {
          return Promise.resolve(
            signedOut
              ? new Response(null, { status: 401 })
              : new Response(JSON.stringify(meResponse), { status: 200 }),
          );
        }
        if (url === '/auth/signout' && init?.method === 'POST') {
          signedOut = true;
          return Promise.resolve(new Response('', { status: 200 }));
        }
        if (url === '/api/me/assessment') {
          return Promise.resolve(new Response('', { status: 404 }));
        }
        if (url === '/auth/signin') {
          // SignInScreen's own mount fetch once the picker is showing.
          return Promise.resolve(new Response(JSON.stringify([]), { status: 200 }));
        }
        return Promise.reject(new Error(`unexpected fetch ${String(init?.method)} ${url}`));
      }),
    );

    render(<App />);

    expect(await screen.findByText(strings.appName)).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: strings.shell.signOut }));

    expect(await screen.findByText(strings.signIn.title)).toBeTruthy();
    await waitFor(() => expect(screen.queryByText(strings.appName)).toBeNull());
    expect(screen.queryByRole('button', { name: strings.shell.signOut })).toBeNull();
  });
});
