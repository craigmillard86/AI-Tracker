import { render } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { TrendSparkline } from '../components/TrendSparkline/TrendSparkline';

describe('TrendSparkline (DESIGN.md A8)', () => {
  it('renders a decorative (aria-hidden) polyline for two or more points', () => {
    const { container } = render(<TrendSparkline values={[1, 1.5, 2]} />);
    const svg = container.querySelector('svg');
    expect(svg).toBeTruthy();
    expect(svg?.getAttribute('aria-hidden')).toBe('true');
    expect(container.querySelector('polyline')).toBeTruthy();
  });

  it('renders nothing for fewer than two points (values live in adjacent text)', () => {
    const { container } = render(<TrendSparkline values={[2]} />);
    expect(container.querySelector('svg')).toBeNull();
  });
});
