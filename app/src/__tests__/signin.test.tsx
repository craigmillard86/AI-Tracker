import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { axe } from 'vitest-axe';
import { SignInScreen } from '../screens/signin/SignInScreen';
import { strings } from '../strings';

const options = [
  {
    role: 'Individual',
    external_ref: 'HAP-SEED-IND',
    name: 'Ada Lovelace',
    email: 'ada@synth.local',
    bu_code: 'BU01',
  },
  {
    role: 'Manager',
    external_ref: 'HAP-SEED-MGR',
    name: 'Grace Hopper',
    email: 'grace@synth.local',
    bu_code: 'BU01',
  },
];

function installFetchMock(impl: (url: string, init?: RequestInit) => Response | Promise<Response>): void {
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      return Promise.resolve(impl(url, init));
    }),
  );
}

describe('SignInScreen (FR-055 role picker)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders every seeded option as a card, plus the static copy from the strings module', async () => {
    installFetchMock(() => new Response(JSON.stringify(options), { status: 200 }));

    render(<SignInScreen onSignedIn={() => {}} />);

    expect(await screen.findByText('Individual')).toBeTruthy();
    expect(screen.getByText('Manager')).toBeTruthy();
    expect(screen.getByText('Ada Lovelace')).toBeTruthy();
    expect(screen.getByText('Grace Hopper')).toBeTruthy();
    expect(screen.getByText(strings.signIn.title)).toBeTruthy();
    expect(screen.getByText(strings.signIn.subtitle)).toBeTruthy();
  });

  it('shows the load error message when the sign-in list fails to load', async () => {
    installFetchMock(() => new Response('', { status: 500 }));

    render(<SignInScreen onSignedIn={() => {}} />);

    expect(await screen.findByText(strings.signIn.loadError)).toBeTruthy();
  });

  it('POSTs the chosen user key and calls onSignedIn on success', async () => {
    const onSignedIn = vi.fn();
    let postedBody: unknown = null;

    installFetchMock((url, init) => {
      if (url === '/auth/signin' && init?.method === 'POST') {
        postedBody = init.body ? JSON.parse(init.body as string) : null;
        return new Response(JSON.stringify({ personId: 'abc' }), { status: 200 });
      }
      if (url === '/auth/signin') {
        return new Response(JSON.stringify(options), { status: 200 });
      }
      throw new Error(`unexpected fetch ${String(init?.method)} ${url}`);
    });

    render(<SignInScreen onSignedIn={onSignedIn} />);

    const card = await screen.findByRole('button', { name: /Ada Lovelace/i });
    fireEvent.click(card);

    await waitFor(() => expect(onSignedIn).toHaveBeenCalledTimes(1));
    expect(postedBody).toEqual({ userKey: 'HAP-SEED-IND' });
  });

  it('shows the sign-in error message when the POST fails, and re-enables the cards', async () => {
    installFetchMock((url, init) => {
      if (url === '/auth/signin' && init?.method === 'POST') {
        return new Response('', { status: 400 });
      }
      return new Response(JSON.stringify(options), { status: 200 });
    });

    render(<SignInScreen onSignedIn={() => {}} />);

    const card = await screen.findByRole('button', { name: /Ada Lovelace/i });
    fireEvent.click(card);

    expect(await screen.findByText(strings.signIn.signInError)).toBeTruthy();
    expect((card as HTMLButtonElement).disabled).toBe(false);
  });

  it('has no detectable accessibility violations', async () => {
    installFetchMock(() => new Response(JSON.stringify(options), { status: 200 }));

    const { container } = render(<SignInScreen onSignedIn={() => {}} />);
    await screen.findByText('Individual');

    // color-contrast needs a real canvas (unavailable in jsdom) — same exclusion as AppShell's
    // own axe test; exercised against the running app in later stories.
    const results = await axe(container, { rules: { 'color-contrast': { enabled: false } } });
    expect(results.violations).toEqual([]);
  });
});
