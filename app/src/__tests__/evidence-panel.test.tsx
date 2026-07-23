import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { axe } from 'vitest-axe';
import type { NodeAggregate } from '../api/client';
import { EvidencePanel } from '../components/EvidencePanel/EvidencePanel';
import { strings } from '../strings';

// Deliberately fictional dimension names/wording — Art. II.4 / the framework grep-guard forbids real
// framework content anywhere in app/src, tests included.
function publishedMeasured(): NodeAggregate {
  return {
    nodeType: 'Bu',
    nodeRef: '00000000-0000-0000-0000-000000000001',
    nodeName: 'Fictional BU',
    cycleId: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
    cycleName: 'Test cycle',
    cycleState: 'Open',
    live: true,
    suppressed: false,
    suppressionReason: null,
    figures: {
      n: 6,
      perDimensionMean: { 'dim-one': 2.0, 'dim-two': 1.2 },
      floorLevelDistribution: { '1': 3, '2': 3 },
      completionPct: 0.9,
      unmoderatedPct: 0,
    },
    dimensions: [
      { key: 'dim-one', name: 'Fictional dimension one', displayOrder: 1 },
      { key: 'dim-two', name: 'Fictional dimension two', displayOrder: 2 },
    ],
    trend: [],
    initiatives: null,
  };
}

function suppressedMeasured(): NodeAggregate {
  return {
    ...publishedMeasured(),
    suppressed: true,
    suppressionReason: 'N<4',
    figures: null,
  };
}

describe('EvidencePanel (HAP-15; FR-047; DESIGN.md A8)', () => {
  it('renders the per-dimension bars and the measured-floor headline when published', () => {
    render(
      <EvidencePanel
        measured={publishedMeasured()}
        measuredFloorLevel={1}
        declaredLevel={null}
        declaredVsMeasuredDivergence={null}
      />,
    );

    expect(screen.getByRole('img', { name: /Fictional dimension one/ })).toBeTruthy();
    expect(screen.getByRole('img', { name: /Fictional dimension two/ })).toBeTruthy();
    expect(screen.getByText(strings.buForms.measuredFloorHeadline(1, '1.6'))).toBeTruthy();
  });

  it('renders no dimension bars/figures and only the suppressed copy when the measured aggregate is suppressed', () => {
    render(
      <EvidencePanel
        measured={suppressedMeasured()}
        measuredFloorLevel={null}
        declaredLevel={2}
        declaredVsMeasuredDivergence={null}
      />,
    );

    expect(screen.queryByRole('img')).toBeNull();
    expect(screen.queryByText(/1\.6/)).toBeNull();
    expect(screen.queryByText('2.0')).toBeNull();
    expect(screen.getByText(new RegExp(strings.dashboard.suppressedCellText))).toBeTruthy();
  });

  it('renders a positive divergence sentence when declared is above the measured floor', () => {
    render(
      <EvidencePanel
        measured={publishedMeasured()}
        measuredFloorLevel={1}
        declaredLevel={2}
        declaredVsMeasuredDivergence={1}
      />,
    );

    expect(screen.getByText(strings.buForms.divergenceSentence(2, 1))).toBeTruthy();
    expect(screen.getByText(strings.buForms.divergenceSentence(2, 1)).textContent).toContain('above');
  });

  it('renders a negative divergence sentence when declared is below the measured floor', () => {
    render(
      <EvidencePanel
        measured={publishedMeasured()}
        measuredFloorLevel={2}
        declaredLevel={1}
        declaredVsMeasuredDivergence={-1}
      />,
    );

    expect(screen.getByText(strings.buForms.divergenceSentence(1, -1))).toBeTruthy();
    expect(screen.getByText(strings.buForms.divergenceSentence(1, -1)).textContent).toContain('below');
  });

  it('renders a "matches" sentence at zero divergence, and never renders divergence UI without a declared level', () => {
    const { rerender } = render(
      <EvidencePanel
        measured={publishedMeasured()}
        measuredFloorLevel={1}
        declaredLevel={1}
        declaredVsMeasuredDivergence={0}
      />,
    );
    expect(screen.getByText(strings.buForms.divergenceSentence(1, 0))).toBeTruthy();
    expect(screen.getByText(strings.buForms.divergenceSentence(1, 0)).textContent).toContain('matches');

    rerender(
      <EvidencePanel
        measured={publishedMeasured()}
        measuredFloorLevel={1}
        declaredLevel={null}
        declaredVsMeasuredDivergence={null}
      />,
    );
    expect(screen.queryByText(strings.moderation.agreeChip)).toBeNull();
  });

  it('has no detectable accessibility violations (published, with divergence)', async () => {
    const { container } = render(
      <EvidencePanel
        measured={publishedMeasured()}
        measuredFloorLevel={1}
        declaredLevel={2}
        declaredVsMeasuredDivergence={1}
      />,
    );

    const results = await axe(container, { rules: { 'color-contrast': { enabled: false } } });
    expect(results.violations).toEqual([]);
  });

  it('has no detectable accessibility violations (suppressed)', async () => {
    const { container } = render(
      <EvidencePanel
        measured={suppressedMeasured()}
        measuredFloorLevel={null}
        declaredLevel={null}
        declaredVsMeasuredDivergence={null}
      />,
    );

    const results = await axe(container, { rules: { 'color-contrast': { enabled: false } } });
    expect(results.violations).toEqual([]);
  });
});
