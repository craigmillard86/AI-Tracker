import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { SuppressedCell } from '../components/SuppressedCell/SuppressedCell';
import { strings } from '../strings';

describe('SuppressedCell (DESIGN.md A8 / FR-071)', () => {
  it('renders the reason text, never blank — and there is no prop to leak a figure', () => {
    const { container } = render(<SuppressedCell reason="N<4" />);
    // The component takes only a reason marker — there is structurally no count/mean to render.
    expect(screen.getByText(new RegExp(strings.dashboard.suppressedCellText))).toBeTruthy();
    const text = (container.textContent ?? '').trim();
    expect(text.length).toBeGreaterThan(0);
    // Exactly the em dash + "Suppressed (reason)" label — no aggregate figure appended.
    expect(text).toBe(
      `${strings.dashboard.stubDash}${strings.dashboard.suppressedCellText} (${strings.dashboard.suppressedCellReason('N<4')})`,
    );
  });

  it('distinguishes the complement reason', () => {
    render(<SuppressedCell reason="Complement" variant="block" />);
    expect(screen.getByText(new RegExp(strings.dashboard.suppressedCellReason('Complement')))).toBeTruthy();
  });
});
