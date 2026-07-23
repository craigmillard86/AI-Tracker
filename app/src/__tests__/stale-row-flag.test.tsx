import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { axe } from 'vitest-axe';
import { StaleRowFlag } from '../components/StaleRowFlag/StaleRowFlag';
import { strings } from '../strings';

describe('StaleRowFlag (HAP-13; DESIGN.md A8)', () => {
  it('renders nothing at 7 days or fresher (the amber threshold is > 7)', () => {
    const { container } = render(<StaleRowFlag days={7} />);
    expect(container.querySelector('.stale-flag')).toBeNull();
    expect(container.textContent).toBe('');
  });

  it('renders an amber chip carrying the day count between 8 and 14 days stale', () => {
    const { container } = render(<StaleRowFlag days={11} />);
    expect(container.querySelector('.stale-flag-amber')).toBeTruthy();
    expect(container.querySelector('.stale-flag-red')).toBeNull();
    // Day count is always in the visible text — colour is never the sole signal (A5/A7).
    expect(screen.getByText(strings.register.staleFlag(11))).toBeTruthy();
    expect(container.textContent).toContain('11');
  });

  it('holds amber at exactly 14 days (the red threshold is > 14)', () => {
    const { container } = render(<StaleRowFlag days={14} />);
    expect(container.querySelector('.stale-flag-amber')).toBeTruthy();
    expect(container.querySelector('.stale-flag-red')).toBeNull();
  });

  it('renders a red chip carrying the day count beyond 14 days stale', () => {
    const { container } = render(<StaleRowFlag days={15} />);
    expect(container.querySelector('.stale-flag-red')).toBeTruthy();
    expect(screen.getByText(strings.register.staleFlag(15))).toBeTruthy();
    expect(container.textContent).toContain('15');
  });

  it('has no detectable accessibility violations', async () => {
    const { container } = render(<StaleRowFlag days={20} />);
    const results = await axe(container, { rules: { 'color-contrast': { enabled: false } } });
    expect(results.violations).toEqual([]);
  });
});
