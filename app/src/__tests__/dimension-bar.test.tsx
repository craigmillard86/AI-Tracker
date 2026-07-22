import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { DimensionBar } from '../components/DimensionBar/DimensionBar';
import { levelOfMean } from '../components/DimensionBar/level';

describe('DimensionBar (DESIGN.md A8)', () => {
  it('exposes the mean as an accessible label and prints the value (never colour alone)', () => {
    render(<DimensionBar name="Fictional dimension one" mean={2.1} />);
    const bar = screen.getByRole('img');
    expect(bar.getAttribute('aria-label')).toContain('Fictional dimension one');
    expect(bar.getAttribute('aria-label')).toContain('2.1');
    // The value is printed as visible text at the bar end.
    expect(screen.getByText('2.1')).toBeTruthy();
  });

  it('maps a mean to the nearest maturity level for the ramp fill', () => {
    expect(levelOfMean(0.2)).toBe(0);
    expect(levelOfMean(1.8)).toBe(2);
    expect(levelOfMean(1.4)).toBe(1);
    expect(levelOfMean(3.4)).toBe(3);
    expect(levelOfMean(-1)).toBe(0);
  });

  it('fills with the ramp class of the mean level', () => {
    const { container } = render(<DimensionBar name="Fictional dimension two" mean={2.6} />);
    expect(container.querySelector('.dimension-bar-fill-3')).toBeTruthy();
  });
});
