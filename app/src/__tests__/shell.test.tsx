import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { axe } from 'vitest-axe';
import { AppShell } from '../shell/AppShell';
import { strings } from '../strings';

describe('AppShell', () => {
  it('renders the brand, primary nav, and content landmark', () => {
    render(<AppShell />);

    expect(screen.getByText(strings.appName)).toBeTruthy();
    expect(screen.getByRole('navigation', { name: strings.shell.primaryNav })).toBeTruthy();
    expect(screen.getByRole('main')).toBeTruthy();
    expect(screen.getByRole('heading', { level: 1, name: strings.home.title })).toBeTruthy();
    for (const item of Object.values(strings.nav)) {
      expect(screen.getByRole('link', { name: item })).toBeTruthy();
    }
  });

  it('has no detectable accessibility violations', async () => {
    const { container } = render(<AppShell />);
    // color-contrast needs a real canvas (unavailable in jsdom); it is exercised
    // against the running app by axe-driven checks in later UI stories.
    const results = await axe(container, {
      rules: { 'color-contrast': { enabled: false } },
    });
    expect(results.violations).toEqual([]);
  });
});
