import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { axe } from 'vitest-axe';
import { DashboardScreen } from '../screens/dashboard-bu/DashboardScreen';
import { strings } from '../strings';

// Deliberately fictional dimension wording — Art. II.4 / the framework grep-guard forbids real
// framework content anywhere in app/src, tests included.
function publishedNode() {
  return {
    nodeType: 'Team',
    nodeRef: '00000000-0000-0000-0000-000000000001',
    nodeName: 'Fictional Unit',
    cycleId: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
    cycleName: 'Test cycle',
    cycleState: 'Open',
    live: true,
    suppressed: false,
    suppressionReason: null,
    figures: {
      n: 4,
      perDimensionMean: { 'dim-one': 2.0, 'dim-two': 1.0 },
      floorLevelDistribution: { '1': 1, '2': 3 },
      completionPct: 0.8,
      unmoderatedPct: 0,
    },
    dimensions: [
      { key: 'dim-one', name: 'Fictional dimension one', displayOrder: 1 },
      { key: 'dim-two', name: 'Fictional dimension two', displayOrder: 2 },
    ],
    trend: [
      {
        cycleId: 'p1',
        cycleName: 'Prior cycle',
        suppressed: false,
        suppressionReason: null,
        figures: {
          n: 4,
          perDimensionMean: { 'dim-one': 1.0, 'dim-two': 1.0 },
          floorLevelDistribution: { '1': 4 },
          completionPct: 0.5,
          unmoderatedPct: 0,
        },
      },
    ],
    initiatives: null,
  };
}

function suppressedNode() {
  return {
    nodeType: 'Team',
    nodeRef: '00000000-0000-0000-0000-000000000002',
    nodeName: 'Tiny Unit',
    cycleId: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
    cycleName: 'Test cycle',
    cycleState: 'Open',
    live: true,
    suppressed: true,
    suppressionReason: 'N<4',
    figures: null,
    dimensions: [{ key: 'dim-one', name: 'Fictional dimension one', displayOrder: 1 }],
    trend: [],
    initiatives: null,
  };
}

// A team half-stuck at L0: per-dimension means are 1.0 (which round(mean) would call "Floor L1"), but the
// per-person floor distribution shows 50% never left L0 — the BB1 trap. The KPI must read from the
// distribution, so it shows Floor L0 and "50% at L0".
function halfL0Node() {
  return {
    ...publishedNode(),
    nodeName: 'Half L0 Unit',
    figures: {
      n: 4,
      perDimensionMean: { 'dim-one': 1.0, 'dim-two': 1.0 },
      floorLevelDistribution: { '0': 2, '1': 2 },
      completionPct: 1.0,
      unmoderatedPct: 0,
    },
    trend: [],
  };
}

function installFetchMock(status: number, body?: unknown): void {
  vi.stubGlobal(
    'fetch',
    vi.fn(() =>
      Promise.resolve(
        body === undefined
          ? new Response('', { status })
          : new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }),
      ),
    ),
  );
}

describe('DashboardScreen (HAP-11; dashboard-bu mockup)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders the published mockup: title, per-dimension bars, floor level, completion, trend', async () => {
    installFetchMock(200, publishedNode());
    render(<DashboardScreen />);

    expect(await screen.findByRole('heading', { level: 1, name: 'Fictional Unit' })).toBeTruthy();

    // Seven-style per-dimension bars (two here) — each an accessible img with the printed value.
    const barOne = screen.getByRole('img', { name: /Fictional dimension one/ });
    expect(barOne.getAttribute('aria-label')).toContain('2.0');
    expect(screen.getByRole('img', { name: /Fictional dimension two/ })).toBeTruthy();

    // Completion tile (80%).
    expect(screen.getByText('80%')).toBeTruthy();

    // Mean maturity trend up: current (2.0+1.0)/2 = 1.5 vs prior 1.0 → +0.5.
    expect(screen.getByText(new RegExp(strings.dashboard.trendUp('0.5').replace(/[▲+]/g, '\\$&')))).toBeTruthy();

    // Initiative counts are stubbed with an em dash (FR-041 — HAP-13).
    expect(screen.getAllByText(strings.dashboard.stubDash).length).toBeGreaterThan(0);
  });

  it('derives the floor KPI from the per-person distribution, not a rounded mean (BB1)', async () => {
    installFetchMock(200, halfL0Node());
    render(<DashboardScreen />);

    await screen.findByRole('heading', { level: 1, name: 'Half L0 Unit' });

    // Honest floor: 50% at L0, so the headline floor is L0 (a rounded mean of 1.0 would wrongly say L1).
    expect(screen.getByText(strings.dashboard.floorBreakdown(50, 50))).toBeTruthy();
    const floorTile = screen.getByText(strings.dashboard.floorLevelLabel).closest('.stat-tile');
    expect(floorTile?.querySelector('.maturity-badge-0')).toBeTruthy();
    expect(floorTile?.querySelector('.maturity-badge-1')).toBeNull();
  });

  it('fires the primary CTA to the self-assessment flow when provided', async () => {
    installFetchMock(200, publishedNode());
    const onStart = vi.fn();
    render(<DashboardScreen onStartSelfAssessment={onStart} />);

    const cta = await screen.findByRole('button', { name: strings.dashboard.startSelfAssessment });
    fireEvent.click(cta);
    expect(onStart).toHaveBeenCalledTimes(1);
  });

  it('renders the suppressed state with no figures (F2 / FR-071)', async () => {
    installFetchMock(200, suppressedNode());
    render(<DashboardScreen />);

    await screen.findByText(strings.dashboard.suppressedTitle);
    expect(screen.getByText(new RegExp(strings.dashboard.suppressedCellText))).toBeTruthy();
    // No completion percentage / figure leaks for a suppressed node.
    expect(screen.queryByText('80%')).toBeNull();
    expect(screen.queryByText(strings.dashboard.completionLabel)).toBeNull();
  });

  it('renders an explicit empty state on a 404 (no open cycle / no team)', async () => {
    installFetchMock(404);
    render(<DashboardScreen />);
    expect(await screen.findByText(strings.dashboard.noDataTitle)).toBeTruthy();
  });

  it('has no detectable accessibility violations (published)', async () => {
    installFetchMock(200, publishedNode());
    const { container } = render(<DashboardScreen />);
    await screen.findByRole('heading', { level: 1, name: 'Fictional Unit' });

    // color-contrast needs a real canvas (unavailable in jsdom); exercised against the running app.
    const results = await axe(container, { rules: { 'color-contrast': { enabled: false } } });
    expect(results.violations).toEqual([]);
  });

  it('has no detectable accessibility violations (suppressed)', async () => {
    installFetchMock(200, suppressedNode());
    const { container } = render(<DashboardScreen />);
    await screen.findByText(strings.dashboard.suppressedTitle);

    const results = await axe(container, { rules: { 'color-contrast': { enabled: false } } });
    expect(results.violations).toEqual([]);
  });
});
