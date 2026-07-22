import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { axe } from 'vitest-axe';
import { DivergenceFlag } from '../components/DivergenceFlag/DivergenceFlag';
import { strings } from '../strings';

describe('DivergenceFlag (HAP-9; FR-009/012; DESIGN.md A2/A8)', () => {
  it('shows the "agree" chip when self and manager scores match (divergence 0)', () => {
    render(<DivergenceFlag selfScore={2} managerScore={2} />);

    expect(screen.getByText(strings.moderation.agreeChip)).toBeTruthy();
    expect(screen.queryByText(strings.moderation.divergenceValue(0))).toBeFalsy();
  });

  it('shows a subtle flag with the numeric divergence at divergence 1', () => {
    const { container } = render(<DivergenceFlag selfScore={1} managerScore={2} />);

    expect(screen.getByText(strings.moderation.divergenceValue(1))).toBeTruthy();
    expect(container.querySelector('.divergence-flag-mid')).toBeTruthy();
  });

  it('shows a red RAG-style flag with the numeric divergence at divergence >= 2', () => {
    const { container } = render(<DivergenceFlag selfScore={3} managerScore={1} />);

    expect(screen.getByText(strings.moderation.divergenceValue(2))).toBeTruthy();
    expect(container.querySelector('.divergence-flag-high')).toBeTruthy();
  });

  it('always prints the divergence as text — colour is never the sole signal', () => {
    // Every variant carries visible text content, not just a coloured swatch (DESIGN.md A5/A7).
    const agree = render(<DivergenceFlag selfScore={0} managerScore={0} />);
    expect(agree.container.textContent?.trim().length).toBeGreaterThan(0);

    const high = render(<DivergenceFlag selfScore={0} managerScore={3} />);
    expect(high.container.textContent).toContain('3');
  });

  it('has no detectable accessibility violations', async () => {
    const { container } = render(<DivergenceFlag selfScore={0} managerScore={3} />);

    const results = await axe(container, { rules: { 'color-contrast': { enabled: false } } });
    expect(results.violations).toEqual([]);
  });
});
