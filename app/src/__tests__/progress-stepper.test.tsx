import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ProgressStepper } from '../components/ProgressStepper/ProgressStepper';
import { strings } from '../strings';

describe('ProgressStepper (DESIGN.md A8)', () => {
  it('renders the 5-of-7 incomplete state with a projected floor of L0 (mockup binding state)', () => {
    render(<ProgressStepper scored={5} total={7} floorLevel={0} />);

    expect(screen.getByText('5')).toBeTruthy();
    expect(screen.getByText('7')).toBeTruthy();
    expect(screen.getByText(strings.assessment.levelAbbrev(0))).toBeTruthy();
    expect(screen.getByText(strings.assessment.progressHintIncomplete(2))).toBeTruthy();
    // The floor level number is always printed — never colour-only (DESIGN.md A5/A7).
    expect(screen.getByText(strings.assessment.floorLevelLabel)).toBeTruthy();
  });

  it('announces dimension progress via an aria-live region', () => {
    render(<ProgressStepper scored={5} total={7} floorLevel={0} />);

    expect(screen.getByText(strings.assessment.progressAnnouncement(5, 7)).closest('[aria-live]')).toBeTruthy();
  });

  it('shows the not-yet-scored state when nothing has been scored', () => {
    render(<ProgressStepper scored={0} total={7} floorLevel={null} />);

    expect(screen.getByText(strings.assessment.notYetScored)).toBeTruthy();
    expect(screen.getByText(strings.assessment.progressHintIncomplete(7))).toBeTruthy();
  });

  it('shows the complete hint once every dimension is scored', () => {
    render(<ProgressStepper scored={7} total={7} floorLevel={1} />);

    expect(screen.getByText(strings.assessment.progressHintComplete)).toBeTruthy();
  });
});
