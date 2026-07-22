import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { StatTile } from '../components/StatTile/StatTile';

describe('StatTile (DESIGN.md A8)', () => {
  it('renders the label, value and sub-text', () => {
    render(<StatTile label="Test label" value="1.6" sub="a sub note" />);
    expect(screen.getByText('Test label')).toBeTruthy();
    expect(screen.getByText('1.6')).toBeTruthy();
    expect(screen.getByText('a sub note')).toBeTruthy();
  });

  it('carries trend direction with an arrow glyph AND text, not colour alone (A5)', () => {
    render(<StatTile label="Mean" value="1.6" trend={{ direction: 'up', text: 'up by a lot' }} />);
    const trend = screen.getByText(/up by a lot/);
    // The arrow glyph is present alongside the text.
    expect(trend.textContent).toContain('▲');
    expect(trend.textContent).toContain('up by a lot');
  });
});
