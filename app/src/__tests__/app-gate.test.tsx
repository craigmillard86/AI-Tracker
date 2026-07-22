import { render, screen } from '@testing-library/react';
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
});
