import { render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { InitiativeDetailResponse } from '../api/client';
import { RegisterDetailScreen } from '../screens/register-detail/RegisterDetailScreen';

/**
 * QA-window adversarial coverage for HAP-14 (fresh-instance QA pass, CLAUDE.md §9), added during QA
 * rather than Dev — attributed as QA work. `register-detail-screen.test.tsx` (Dev's own suite) already
 * proves the overdue callout appears at 11 days stale for an active stage and is suppressed for Idea and
 * Retired at the same 11-day staleness. This file targets the exact >7-day BOUNDARY the panel's fix
 * (`days > 7 && OVERDUE_ELIGIBLE_STAGES.includes(...)`) introduces but Dev's own suite never pins down at
 * the edge: is day 7 itself (not-yet-overdue) genuinely excluded, and is day 8 (the FIRST overdue day)
 * genuinely included? data-model.md: "Overdue = active stage (Evaluation→Scaled) and no update in 7 days"
 * — root spec.md: "no update in 8+ days" is the trigger, so exactly-7 must NOT show the callout and
 * exactly-8 MUST.
 *
 * Deliberately fictional wording — Art. II.4 grep-guard forbids real taxonomy/framework content in
 * app/src (tests included). BU/category names and initiative content here are invented.
 */
const BU1 = '11111111-1111-1111-1111-111111111111';
const CAT1 = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const INITIATIVE_ID = 'i-1';

/** ISO timestamp `days` (+1h buffer) in the past, so `Math.floor` lands cleanly on `days` — mirrors the
 * sibling Dev test file's own `daysAgoIso` helper exactly. */
function daysAgoIso(days: number): string {
  return new Date(Date.now() - (days * 86400000 + 3600000)).toISOString();
}

function detail(overrides: Partial<InitiativeDetailResponse> = {}): InitiativeDetailResponse {
  return {
    id: INITIATIVE_ID,
    businessUnitId: BU1,
    name: 'Fictional Boundary Initiative',
    description: null,
    sponsorPersonId: null,
    ownerPersonId: 'owner-1',
    createdByPersonId: 'creator-1',
    registeredAt: daysAgoIso(120),
    categoryId: CAT1,
    aiDlcLevel: 2,
    functionsAffected: [],
    dimensionsAdvanced: [],
    currentStage: 'Pilot',
    harrisStage: 'Development',
    ragStatus: 'OnTrack',
    lastUpdateAt: daysAgoIso(7),
    customersInProduction: null,
    riskTier: 'Low',
    dataSensitivity: 'None',
    regulatoryRelevance: [],
    approvalStatus: null,
    approver: null,
    oversightModel: null,
    governanceNotes: null,
    modelsProviders: [],
    vendorsTools: [],
    usesCogito: false,
    canEdit: true,
    categoryCustomerDeployed: false,
    stageHistory: [
      { id: 'sh-1', stage: 'Idea', priorStage: null, enteredAt: daysAgoIso(120), enteredBy: 'owner-1' },
      { id: 'sh-2', stage: 'Pilot', priorStage: 'Idea', enteredAt: daysAgoIso(60), enteredBy: 'owner-1' },
    ],
    nrLines: [],
    updates: [],
    ...overrides,
  };
}

function installFetchMock(record: InitiativeDetailResponse): void {
  vi.stubGlobal(
    'fetch',
    vi.fn((input: string) => {
      const url = String(input);
      const json = (body: unknown, status = 200): Promise<Response> =>
        Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }));
      if (url.startsWith('/api/business-units')) {
        return json([{ id: BU1, code: 'FBU1', name: 'Fictional BU One' }]);
      }
      if (url.startsWith('/api/harris-categories')) {
        return json([{ id: CAT1, key: 'cat-a', name: 'Fictional Category A', groupReported: true, customerDeployed: false }]);
      }
      if (url === `/api/initiatives/${INITIATIVE_ID}`) {
        return json(record);
      }
      return Promise.resolve(new Response('', { status: 404 }));
    }),
  );
}

describe('RegisterDetailScreen overdue-banner 7/8-day boundary (HAP-14 QA)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('does NOT show the overdue callout at exactly 7 days stale, for an active (overdue-eligible) stage', async () => {
    installFetchMock(detail({ currentStage: 'Pilot', lastUpdateAt: daysAgoIso(7) }));
    render(<RegisterDetailScreen initiativeId={INITIATIVE_ID} onBack={vi.fn()} />);
    await screen.findByRole('heading', { level: 1, name: 'Fictional Boundary Initiative' });

    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('DOES show the overdue callout at exactly 8 days stale (the first overdue day), for an active stage', async () => {
    installFetchMock(detail({ currentStage: 'Pilot', lastUpdateAt: daysAgoIso(8) }));
    render(<RegisterDetailScreen initiativeId={INITIATIVE_ID} onBack={vi.fn()} />);
    await screen.findByRole('heading', { level: 1, name: 'Fictional Boundary Initiative' });

    const callout = await screen.findByRole('alert');
    expect(callout.textContent).toContain('8');
  });

  it('does NOT show the overdue callout for an Evaluation-stage initiative at exactly 7 days stale', async () => {
    installFetchMock(detail({ currentStage: 'Evaluation', lastUpdateAt: daysAgoIso(7) }));
    render(<RegisterDetailScreen initiativeId={INITIATIVE_ID} onBack={vi.fn()} />);
    await screen.findByRole('heading', { level: 1, name: 'Fictional Boundary Initiative' });

    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('DOES show the overdue callout for a Scaled-stage initiative (the last overdue-eligible stage) at 8 days stale', async () => {
    installFetchMock(detail({
      currentStage: 'Scaled',
      lastUpdateAt: daysAgoIso(8),
      stageHistory: [
        { id: 'sh-1', stage: 'Idea', priorStage: null, enteredAt: daysAgoIso(120), enteredBy: 'owner-1' },
        { id: 'sh-2', stage: 'Scaled', priorStage: 'Idea', enteredAt: daysAgoIso(60), enteredBy: 'owner-1' },
      ],
    }));
    render(<RegisterDetailScreen initiativeId={INITIATIVE_ID} onBack={vi.fn()} />);
    await screen.findByRole('heading', { level: 1, name: 'Fictional Boundary Initiative' });

    expect(await screen.findByRole('alert')).toBeTruthy();
  });
});
