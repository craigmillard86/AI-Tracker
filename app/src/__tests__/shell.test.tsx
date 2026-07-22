import { render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { axe } from 'vitest-axe';
import { AppShell } from '../shell/AppShell';
import { strings } from '../strings';

/** AppShell now mounts AssessmentSelfScreen (HAP-8) as its content — mock the fetch it issues on
 * mount. A 404 (no open cycle) keeps the rendered tree to shell chrome + strings-table literals
 * only, which is what these shell-level tests care about. */
function installNoCycleFetchMock(): void {
  vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(new Response('', { status: 404 }))));
}

describe('AppShell', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders the brand, primary nav, and content landmark', async () => {
    installNoCycleFetchMock();

    render(<AppShell />);

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

    const { container } = render(<AppShell />);
    await screen.findByText(strings.assessment.noOpenCycleTitle);

    // color-contrast needs a real canvas (unavailable in jsdom); it is exercised
    // against the running app by axe-driven checks in later UI stories.
    const results = await axe(container, {
      rules: { 'color-contrast': { enabled: false } },
    });
    expect(results.violations).toEqual([]);
  });
});
