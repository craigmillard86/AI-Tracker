import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { axe } from 'vitest-axe';
import { BuFormsScreen } from '../screens/bu-forms/BuFormsScreen';
import { strings } from '../strings';

// Deliberately fictional dimension/BU wording — Art. II.4 / the framework grep-guard forbids real
// framework or taxonomy content anywhere in app/src, tests included.
const BU_ID = '33333333-3333-3333-3333-333333333333';

function dashboardBuNode() {
  return {
    nodeType: 'Bu',
    nodeRef: BU_ID,
    nodeName: 'Fictional BU',
    cycleId: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
    cycleName: 'Test cycle',
    cycleState: 'Open',
    live: true,
    suppressed: false,
    suppressionReason: null,
    figures: { n: 6, perDimensionMean: {}, floorLevelDistribution: {}, completionPct: 1, unmoderatedPct: 0 },
    dimensions: [],
    trend: [],
    initiatives: null,
  };
}

function dashboardTeamNode() {
  return { ...dashboardBuNode(), nodeType: 'Team' };
}

function measuredAggregate() {
  return {
    nodeType: 'Bu',
    nodeRef: BU_ID,
    nodeName: 'Fictional BU',
    cycleId: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
    cycleName: 'Test cycle',
    cycleState: 'Open',
    live: true,
    suppressed: false,
    suppressionReason: null,
    figures: {
      n: 6,
      perDimensionMean: { 'dim-one': 1.6 },
      floorLevelDistribution: { '1': 6 },
      completionPct: 0.9,
      unmoderatedPct: 0,
    },
    dimensions: [{ key: 'dim-one', name: 'Fictional dimension one', displayOrder: 1 }],
    trend: [],
    initiatives: null,
  };
}

function declarationsResponse(overrides: Record<string, unknown> = {}) {
  return {
    businessUnitId: BU_ID,
    declarations: [
      {
        id: 'decl-1',
        businessUnitId: BU_ID,
        weekOf: '2026-07-13',
        declaredLevel: 2,
        nextLevelExpectedDate: '2028-04-01',
        ragStatus: 'AtRisk',
        note: 'Prior fictional note',
        declaredBy: 'Prior User',
        createdAt: '2026-07-14T00:00:00Z',
        updatedAt: '2026-07-14T00:00:00Z',
      },
    ],
    measured: measuredAggregate(),
    measuredFloorLevel: 1,
    declaredVsMeasuredDivergence: 1,
    ...overrides,
  };
}

function metricsResponse(overrides: Record<string, unknown> = {}) {
  return {
    businessUnitId: BU_ID,
    month: '2026-07-01',
    supportInternal: { timeSavingsPct: 18, fewerPeopleNeeded: 'No — reallocated', supportRatioImpact: '1:340 -> 1:410' },
    supportCustomer: { customersYtd: 612, ticketsYtd: 48900, resolvedByAiYtd: 9240, aiAssistedYtd: 21500 },
    sorCalledByOtherApps: null,
    carriedForward: true,
    submittedBy: null,
    createdAt: null,
    ...overrides,
  };
}

interface MockOptions {
  dashboardNode?: unknown;
  declarations?: unknown;
  metrics?: unknown;
  declarationPostStatus?: number;
  metricsPostStatus?: number;
}

function installFetchMock(options: MockOptions = {}): {
  calls: Array<{ url: string; method: string; body?: Record<string, unknown> }>;
} {
  const calls: Array<{ url: string; method: string; body?: Record<string, unknown> }> = [];
  const json = (body: unknown, status = 200): Promise<Response> =>
    Promise.resolve(
      new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }),
    );

  vi.stubGlobal(
    'fetch',
    vi.fn((input: string, init?: RequestInit) => {
      const url = String(input);
      const method = init?.method ?? 'GET';
      const body = init?.body ? (JSON.parse(String(init.body)) as Record<string, unknown>) : undefined;
      calls.push({ url, method, body });

      if (url === '/api/me/dashboard' && method === 'GET') {
        if (options.dashboardNode === null) {
          return Promise.resolve(new Response('', { status: 404 }));
        }
        return json(options.dashboardNode ?? dashboardBuNode());
      }
      if (url === `/api/bus/${BU_ID}/declarations` && method === 'GET') {
        if (options.declarations === null) {
          return Promise.resolve(new Response('', { status: 404 }));
        }
        return json(options.declarations ?? declarationsResponse());
      }
      if (url === `/api/bus/${BU_ID}/declarations` && method === 'POST') {
        const status = options.declarationPostStatus ?? 201;
        if (status !== 200 && status !== 201) {
          return Promise.resolve(new Response('', { status }));
        }
        return json(
          {
            id: 'decl-new',
            businessUnitId: BU_ID,
            weekOf: body?.weekOf,
            declaredLevel: body?.declaredLevel,
            nextLevelExpectedDate: body?.nextLevelExpectedDate ?? null,
            ragStatus: body?.ragStatus,
            note: body?.note ?? null,
            declaredBy: 'Current User',
            createdAt: new Date().toISOString(),
            updatedAt: new Date().toISOString(),
          },
          status,
        );
      }
      if (url.startsWith(`/api/bus/${BU_ID}/metrics`) && method === 'GET') {
        if (options.metrics === null) {
          return Promise.resolve(new Response('', { status: 404 }));
        }
        return json(options.metrics ?? metricsResponse());
      }
      if (url === `/api/bus/${BU_ID}/metrics` && method === 'POST') {
        const status = options.metricsPostStatus ?? 200;
        if (status !== 200 && status !== 201) {
          return Promise.resolve(new Response('', { status }));
        }
        return json(
          {
            businessUnitId: BU_ID,
            month: body?.month,
            supportInternal: body?.supportInternal,
            supportCustomer: body?.supportCustomer,
            sorCalledByOtherApps: body?.sorCalledByOtherApps ?? null,
            carriedForward: false,
            submittedBy: 'Current User',
            createdAt: new Date().toISOString(),
          },
          status,
        );
      }
      return Promise.resolve(new Response('', { status: 404 }));
    }),
  );
  return { calls };
}

describe('BuFormsScreen (HAP-15; FR-047/FR-048; bu-forms mockup)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('resolves the caller BU via the dashboard and loads both forms', async () => {
    installFetchMock();
    render(<BuFormsScreen />);

    expect(await screen.findByRole('heading', { level: 1, name: strings.buForms.pageTitle })).toBeTruthy();
    expect(screen.getByText(strings.buForms.declarationCardTitle)).toBeTruthy();
    expect(screen.getByText(strings.buForms.metricsCardTitle)).toBeTruthy();

    // EvidencePanel dimension bar rendered from the measured aggregate.
    expect(screen.getByRole('img', { name: /Fictional dimension one/ })).toBeTruthy();

    // Prior declaration prefilled the level picker and RAG choice.
    expect(screen.getByLabelText(strings.buForms.declaredLevelOptionLabel(2))).toHaveProperty('checked', true);
    expect(screen.getByRole('radio', { name: strings.buForms.ragLabels.AtRisk })).toHaveProperty('checked', true);

    // Metrics YTD fields pre-filled from the fetched response.
    expect(screen.getByLabelText(strings.buForms.customersYtdLabel)).toHaveValue(612);
    expect(screen.getByLabelText(strings.buForms.ticketsYtdLabel)).toHaveValue(48900);
  });

  it('renders the no-BU-role empty state for a caller whose dashboard resolves to a non-Bu node', async () => {
    installFetchMock({ dashboardNode: dashboardTeamNode() });
    render(<BuFormsScreen />);

    expect(await screen.findByText(strings.buForms.noRoleBody)).toBeTruthy();
    expect(screen.queryByText(strings.buForms.declarationCardTitle)).toBeNull();
  });

  it('renders the no-BU-role empty state when the dashboard has nothing to show (404)', async () => {
    installFetchMock({ dashboardNode: null });
    render(<BuFormsScreen />);

    expect(await screen.findByText(strings.buForms.noRoleBody)).toBeTruthy();
  });

  it('posts a weekly declaration on the happy path and shows it in the history', async () => {
    installFetchMock();
    render(<BuFormsScreen />);
    await screen.findByRole('heading', { level: 1, name: strings.buForms.pageTitle });

    fireEvent.click(screen.getByLabelText(strings.buForms.declaredLevelOptionLabel(3)));
    fireEvent.click(screen.getByRole('button', { name: strings.buForms.saveDeclarationButton }));

    expect(await screen.findByText(strings.buForms.declarationSaveSuccess)).toBeTruthy();
    const historyBadges = screen.getAllByText(strings.assessment.levelAbbrev(3));
    expect(historyBadges.length).toBeGreaterThan(0);
  });

  it('shows a 403-specific inline error when the caller is not the BU Lead/delegate', async () => {
    installFetchMock({ declarationPostStatus: 403 });
    render(<BuFormsScreen />);
    await screen.findByRole('heading', { level: 1, name: strings.buForms.pageTitle });

    fireEvent.click(screen.getByRole('button', { name: strings.buForms.saveDeclarationButton }));

    expect(await screen.findByText(strings.buForms.declarationSaveForbiddenError)).toBeTruthy();
  });

  it('shows a 422-specific inline error on a validation failure', async () => {
    installFetchMock({ declarationPostStatus: 422 });
    render(<BuFormsScreen />);
    await screen.findByRole('heading', { level: 1, name: strings.buForms.pageTitle });

    fireEvent.click(screen.getByRole('button', { name: strings.buForms.saveDeclarationButton }));

    expect(await screen.findByText(strings.buForms.declarationSaveValidationError)).toBeTruthy();
  });

  it('shows the carried-forward YTD values with the hint, and renders SOR empty even though a prior declaration/metrics value existed', async () => {
    installFetchMock({ metrics: metricsResponse({ carriedForward: true, sorCalledByOtherApps: null }) });
    render(<BuFormsScreen />);
    await screen.findByRole('heading', { level: 1, name: strings.buForms.pageTitle });

    expect(screen.getByText(strings.buForms.carriedForwardHint)).toBeTruthy();
    expect(screen.getByLabelText(strings.buForms.customersYtdLabel)).toHaveValue(612);
    // SOR is current-month-only — the server sent null, so the field renders empty, never a stale value.
    expect(screen.getByLabelText(strings.buForms.sorFieldLabel)).toHaveValue('');
  });

  it('does not show the carried-forward hint when the current month already has a saved row', async () => {
    installFetchMock({ metrics: metricsResponse({ carriedForward: false }) });
    render(<BuFormsScreen />);
    await screen.findByRole('heading', { level: 1, name: strings.buForms.pageTitle });

    expect(screen.queryByText(strings.buForms.carriedForwardHint)).toBeNull();
  });

  it('posts monthly metrics on the happy path', async () => {
    installFetchMock();
    render(<BuFormsScreen />);
    await screen.findByRole('heading', { level: 1, name: strings.buForms.pageTitle });

    fireEvent.change(screen.getByLabelText(strings.buForms.sorFieldLabel), {
      target: { value: 'Yes — 2 fictional consumers' },
    });
    fireEvent.click(screen.getByRole('button', { name: strings.buForms.saveMetricsButton }));

    await waitFor(() => expect(screen.getByText(strings.buForms.metricsSaveSuccess)).toBeTruthy());
  });

  it('shows a 403-specific inline error for the metrics form', async () => {
    installFetchMock({ metricsPostStatus: 403 });
    render(<BuFormsScreen />);
    await screen.findByRole('heading', { level: 1, name: strings.buForms.pageTitle });

    fireEvent.click(screen.getByRole('button', { name: strings.buForms.saveMetricsButton }));

    expect(await screen.findByText(strings.buForms.metricsSaveForbiddenError)).toBeTruthy();
  });

  it('has no detectable accessibility violations on the ready state', async () => {
    installFetchMock();
    const { container } = render(<BuFormsScreen />);
    await screen.findByRole('heading', { level: 1, name: strings.buForms.pageTitle });

    const results = await axe(container, { rules: { 'color-contrast': { enabled: false } } });
    expect(results.violations).toEqual([]);
  });
});
