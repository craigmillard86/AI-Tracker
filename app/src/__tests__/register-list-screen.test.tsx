import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { axe } from 'vitest-axe';
import type { InitiativeResponse } from '../api/client';
import { RegisterListScreen } from '../screens/register-list/RegisterListScreen';
import { strings } from '../strings';

// Deliberately fictional wording — Art. II.4 grep-guard forbids real taxonomy/framework content in
// app/src (tests included). BU and category names here are invented, not the seeded Harris taxonomy.
const BU1 = '11111111-1111-1111-1111-111111111111';
const BU2 = '22222222-2222-2222-2222-222222222222';
const CAT1 = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const CAT2 = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

const businessUnits = () => [
  { id: BU1, code: 'FBU1', name: 'Fictional BU One' },
  { id: BU2, code: 'FBU2', name: 'Fictional BU Two' },
];

const categories = () => [
  { id: CAT1, key: 'cat-a', name: 'Fictional Category A', groupReported: true, customerDeployed: true },
  { id: CAT2, key: 'cat-b', name: 'Fictional Category B', groupReported: false, customerDeployed: false },
];

/** ISO timestamp `days` (+1h buffer) in the past, so `Math.floor` lands cleanly on `days`. */
function daysAgoIso(days: number): string {
  return new Date(Date.now() - (days * 86400000 + 3600000)).toISOString();
}

function initiative(overrides: Partial<InitiativeResponse> & { id: string; name: string }): InitiativeResponse {
  return {
    businessUnitId: BU1,
    description: null,
    sponsorPersonId: null,
    ownerPersonId: 'owner',
    createdByPersonId: 'creator',
    registeredAt: daysAgoIso(30),
    categoryId: CAT1,
    aiDlcLevel: 2,
    functionsAffected: [],
    dimensionsAdvanced: [],
    currentStage: 'Pilot',
    harrisStage: 'Development',
    ragStatus: 'OnTrack',
    lastUpdateAt: daysAgoIso(2),
    customersInProduction: 5,
    riskTier: 'Low',
    ...overrides,
  };
}

const defaultInitiatives = (): InitiativeResponse[] => [
  initiative({
    id: 'i-fresh',
    name: 'Fresh Initiative',
    ragStatus: 'OnTrack',
    aiDlcLevel: 2,
    lastUpdateAt: daysAgoIso(2),
    customersInProduction: 14,
  }),
  initiative({
    id: 'i-amber',
    name: 'Amber Stale Initiative',
    businessUnitId: BU2,
    categoryId: CAT2,
    ragStatus: 'AtRisk',
    aiDlcLevel: 1,
    currentStage: 'Idea',
    harrisStage: 'Ideation',
    lastUpdateAt: daysAgoIso(11),
    customersInProduction: null,
  }),
  initiative({
    id: 'i-red',
    name: 'Red Stale Initiative',
    ragStatus: 'OffTrack',
    aiDlcLevel: 3,
    currentStage: 'Retired',
    harrisStage: 'IdeasTriedButStopped',
    lastUpdateAt: daysAgoIso(20),
    customersInProduction: 2,
  }),
];

interface MockOptions {
  initiatives?: InitiativeResponse[];
}

/** Routes fetch by URL and records every call so the tests can assert re-fetch query params. */
function installFetchMock(options: MockOptions = {}): string[] {
  const rows = options.initiatives ?? defaultInitiatives();
  const calls: string[] = [];
  const json = (body: unknown): Promise<Response> =>
    Promise.resolve(
      new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } }),
    );
  vi.stubGlobal(
    'fetch',
    vi.fn((input: string) => {
      const url = String(input);
      calls.push(url);
      if (url.startsWith('/api/business-units')) {
        return json(businessUnits());
      }
      if (url.startsWith('/api/harris-categories')) {
        return json(categories());
      }
      if (url.startsWith('/api/initiatives')) {
        return json(rows);
      }
      return Promise.resolve(new Response('', { status: 404 }));
    }),
  );
  return calls;
}

describe('RegisterListScreen (HAP-13; register-list mockup)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders the DataTable from the mocked reference + initiatives endpoints', async () => {
    installFetchMock();
    render(<RegisterListScreen />);

    expect(await screen.findByRole('heading', { level: 1, name: strings.register.pageTitle })).toBeTruthy();
    // Rows resolve BU + category ids to names (scoped to the table — the names also appear as filter
    // <option>s, which is expected).
    const table = within(screen.getByRole('table'));
    expect(table.getByText('Fresh Initiative')).toBeTruthy();
    expect(table.getAllByText('Fictional BU One').length).toBeGreaterThan(0);
    expect(table.getByText('Fictional Category B')).toBeTruthy();
    // Stage → Harris mapped label (IdeasTriedButStopped renders as its short label).
    expect(table.getByText(strings.register.stageArrow('Retired', strings.register.harrisStageLabels.IdeasTriedButStopped))).toBeTruthy();
  });

  it('level badge always prints the level number and the RAG chip always prints its label', async () => {
    installFetchMock();
    render(<RegisterListScreen />);
    await screen.findByText('Fresh Initiative');

    const table = within(screen.getByRole('table'));
    // Levels 1/2/3 present as text (number always printed — colour is never the sole signal).
    expect(table.getAllByText(strings.assessment.levelAbbrev(2)).length).toBeGreaterThan(0);
    expect(table.getAllByText(strings.assessment.levelAbbrev(3)).length).toBeGreaterThan(0);
    // RAG labels present as row text (the labels also appear as filter <option>s — scope to the table).
    expect(table.getByText(strings.register.ragLabels.OnTrack)).toBeTruthy();
    expect(table.getByText(strings.register.ragLabels.AtRisk)).toBeTruthy();
    expect(table.getByText(strings.register.ragLabels.OffTrack)).toBeTruthy();
  });

  it('flags stale rows amber (>7d) / red (>14d) with the day count in the text, none when fresh', async () => {
    installFetchMock();
    const { container } = render(<RegisterListScreen />);
    await screen.findByText('Fresh Initiative');

    // Fresh row (2d): no stale chip.
    const freshRow = screen.getByText('Fresh Initiative').closest('tr');
    expect(freshRow?.querySelector('.stale-flag')).toBeNull();

    // Amber row (11d): amber chip carrying "11".
    const amberRow = screen.getByText('Amber Stale Initiative').closest('tr')!;
    const amberChip = amberRow.querySelector('.stale-flag-amber');
    expect(amberChip).toBeTruthy();
    expect(amberChip?.textContent).toContain('11');

    // Red row (20d): red chip carrying "20".
    const redRow = screen.getByText('Red Stale Initiative').closest('tr')!;
    const redChip = redRow.querySelector('.stale-flag-red');
    expect(redChip).toBeTruthy();
    expect(redChip?.textContent).toContain('20');

    expect(within(container).getByText(strings.register.staleFlag(11))).toBeTruthy();
  });

  it('re-fetches with the right query param for each server-side facet', async () => {
    const calls = installFetchMock();
    render(<RegisterListScreen />);
    await screen.findByText('Fresh Initiative');

    // BU facet.
    fireEvent.change(screen.getByLabelText(strings.register.buLabel), { target: { value: BU2 } });
    expect(calls.some((u) => u.includes('/api/initiatives?') && u.includes(`bu=${BU2}`))).toBe(true);

    // Category facet.
    fireEvent.change(screen.getByLabelText(strings.register.categoryLabel), { target: { value: CAT1 } });
    expect(calls.some((u) => u.includes('/api/initiatives?') && u.includes(`category=${CAT1}`))).toBe(true);

    // Stage facet (enum wire value).
    fireEvent.change(screen.getByLabelText(strings.register.stageLabel), { target: { value: 'Pilot' } });
    expect(calls.some((u) => u.includes('/api/initiatives?') && u.includes('stage=Pilot'))).toBe(true);

    // Search facet — debounced ~250ms (SHOULD-FIX, L2 panel): not immediate, but arrives shortly after.
    fireEvent.change(screen.getByLabelText(strings.register.searchLabel), { target: { value: 'triage' } });
    expect(calls.some((u) => u.includes('search=triage'))).toBe(false);
    await waitFor(() =>
      expect(calls.some((u) => u.includes('/api/initiatives?') && u.includes('search=triage'))).toBe(true),
    );
  });

  it('applies the RAG filter client-side (the backend has no RAG facet)', async () => {
    installFetchMock();
    render(<RegisterListScreen />);
    await screen.findByText('Fresh Initiative');

    fireEvent.change(screen.getByLabelText(strings.register.ragLabel), { target: { value: 'OffTrack' } });

    // Only the Off Track row survives client-side filtering.
    expect(screen.getByText('Red Stale Initiative')).toBeTruthy();
    expect(screen.queryByText('Fresh Initiative')).toBeNull();
    expect(screen.queryByText('Amber Stale Initiative')).toBeNull();
  });

  it('paginates above 25 rows', async () => {
    const many = Array.from({ length: 30 }, (_, index) =>
      initiative({ id: `i-${index}`, name: `Initiative Number ${index}` }),
    );
    installFetchMock({ initiatives: many });
    render(<RegisterListScreen />);
    await screen.findByText('Initiative Number 0');

    // Page 1 shows the first 25; the 26th is on page 2.
    expect(screen.getByText('Initiative Number 24')).toBeTruthy();
    expect(screen.queryByText('Initiative Number 25')).toBeNull();
    expect(screen.getByText(strings.register.paginationStatus(1, 2))).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: strings.register.paginationNext }));

    expect(screen.getByText('Initiative Number 25')).toBeTruthy();
    expect(screen.queryByText('Initiative Number 0')).toBeNull();
    expect(screen.getByText(strings.register.paginationStatus(2, 2))).toBeTruthy();
  });

  it('does not paginate at or below 25 rows', async () => {
    const rows = Array.from({ length: 25 }, (_, index) =>
      initiative({ id: `i-${index}`, name: `Initiative Number ${index}` }),
    );
    installFetchMock({ initiatives: rows });
    render(<RegisterListScreen />);
    await screen.findByText('Initiative Number 0');

    expect(screen.queryByRole('button', { name: strings.register.paginationNext })).toBeNull();
  });

  it('renders an explicit empty state when no initiatives match', async () => {
    installFetchMock({ initiatives: [] });
    render(<RegisterListScreen />);

    expect(await screen.findByText(strings.register.emptyTitle)).toBeTruthy();
  });

  it('fires the primary CTA when provided', async () => {
    installFetchMock();
    const onNew = vi.fn();
    render(<RegisterListScreen onNewInitiative={onNew} />);
    await screen.findByText('Fresh Initiative');

    fireEvent.click(screen.getByRole('button', { name: strings.register.newInitiative }));
    expect(onNew).toHaveBeenCalledTimes(1);
  });

  it('has no detectable accessibility violations', async () => {
    installFetchMock();
    const { container } = render(<RegisterListScreen />);
    await screen.findByText('Fresh Initiative');

    const results = await axe(container, { rules: { 'color-contrast': { enabled: false } } });
    expect(results.violations).toEqual([]);
  });
});
