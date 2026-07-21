import { render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import { strings } from '../strings';

function installMeFetchMock(status: number, body: unknown): void {
  vi.stubGlobal(
    'fetch',
    vi.fn(() => Promise.resolve(new Response(status === 204 ? null : JSON.stringify(body), { status }))),
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
