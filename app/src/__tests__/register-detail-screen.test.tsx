import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { axe } from 'vitest-axe';
import type { InitiativeDetailResponse, NrLine, WeeklyUpdate } from '../api/client';
import { RegisterDetailScreen } from '../screens/register-detail/RegisterDetailScreen';
import { strings } from '../strings';

// Deliberately fictional wording — Art. II.4 grep-guard forbids real taxonomy/framework content in
// app/src (tests included). BU/category names and initiative content here are invented.
const BU1 = '11111111-1111-1111-1111-111111111111';
const CAT1 = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const INITIATIVE_ID = 'i-1';

const businessUnits = () => [{ id: BU1, code: 'FBU1', name: 'Fictional BU One' }];
const categories = () => [
  { id: CAT1, key: 'cat-a', name: 'Fictional Category A', groupReported: true, customerDeployed: true },
];

/** ISO timestamp `days` (+1h buffer) in the past, so `Math.floor` lands cleanly on `days`. */
function daysAgoIso(days: number): string {
  return new Date(Date.now() - (days * 86400000 + 3600000)).toISOString();
}

function nrLine(overrides: Partial<NrLine> & { id: string }): NrLine {
  return {
    year: 2026,
    direction: 'Direct',
    recurrence: 'Recurring',
    amountUsd: 100000,
    description: 'Fictional NR line',
    locked: false,
    ...overrides,
  };
}

function weeklyUpdate(overrides: Partial<WeeklyUpdate> & { id: string }): WeeklyUpdate {
  return {
    ragStatus: 'OnTrack',
    note: 'Fictional weekly update note',
    createdBy: 'F. Tester',
    createdAt: daysAgoIso(3),
    ...overrides,
  };
}

function detail(overrides: Partial<InitiativeDetailResponse> = {}): InitiativeDetailResponse {
  return {
    id: INITIATIVE_ID,
    businessUnitId: BU1,
    name: 'Fictional Copilot Initiative',
    description: 'A fictional agent that does fictional things.',
    sponsorPersonId: 'sponsor-1',
    ownerPersonId: 'owner-1',
    createdByPersonId: 'creator-1',
    registeredAt: daysAgoIso(120),
    categoryId: CAT1,
    aiDlcLevel: 2,
    functionsAffected: [],
    dimensionsAdvanced: ['fictional-dimension-a', 'fictional-dimension-b'],
    currentStage: 'Pilot',
    harrisStage: 'Development',
    ragStatus: 'OnTrack',
    lastUpdateAt: daysAgoIso(2),
    customersInProduction: 14,
    riskTier: 'High',
    dataSensitivity: 'PHI',
    regulatoryRelevance: ['UK GDPR'],
    approvalStatus: 'Conditional — BU process',
    approver: 'A. Approver',
    oversightModel: 'Human-in-the-loop',
    governanceNotes: null,
    modelsProviders: ['Fictional Model'],
    vendorsTools: ['vendor: internal'],
    usesCogito: false,
    canEdit: true,
    categoryCustomerDeployed: true,
    stageHistory: [
      { id: 'sh-1', stage: 'Idea', priorStage: null, enteredAt: daysAgoIso(120), enteredBy: 'owner-1' },
      { id: 'sh-2', stage: 'Evaluation', priorStage: 'Idea', enteredAt: daysAgoIso(90), enteredBy: 'owner-1' },
      { id: 'sh-3', stage: 'Pilot', priorStage: 'Evaluation', enteredAt: daysAgoIso(60), enteredBy: 'owner-1' },
    ],
    nrLines: [
      nrLine({
        id: 'nr-1',
        direction: 'Direct',
        recurrence: 'Recurring',
        amountUsd: 420000,
        description: 'Fictional NR line one',
        locked: false,
      }),
      nrLine({
        id: 'nr-2',
        direction: 'Indirect',
        recurrence: 'OneTime',
        amountUsd: 85000,
        description: 'Fictional NR line two (locked)',
        locked: true,
      }),
      nrLine({
        id: 'nr-3',
        direction: 'Direct',
        recurrence: 'OneTime',
        amountUsd: 30000,
        description: 'Fictional NR line three (locked server-side after load)',
        locked: false,
      }),
    ],
    updates: [
      weeklyUpdate({ id: 'u-2', ragStatus: 'AtRisk', createdAt: daysAgoIso(3), note: 'Latest update note' }),
      weeklyUpdate({ id: 'u-1', ragStatus: 'OnTrack', createdAt: daysAgoIso(10), note: 'Earlier update note' }),
    ],
    ...overrides,
  };
}

interface MockOptions {
  detail?: InitiativeDetailResponse | null;
  onDeleteNrLine?: (lineId: string) => number;
}

function installFetchMock(options: MockOptions = {}): { calls: Array<{ url: string; method: string }> } {
  const record = options.detail === undefined ? detail() : options.detail;
  const calls: Array<{ url: string; method: string }> = [];
  const json = (body: unknown, status = 200): Promise<Response> =>
    Promise.resolve(
      new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }),
    );

  vi.stubGlobal(
    'fetch',
    vi.fn((input: string, init?: RequestInit) => {
      const url = String(input);
      const method = init?.method ?? 'GET';
      calls.push({ url, method });

      if (url.startsWith('/api/business-units')) {
        return json(businessUnits());
      }
      if (url.startsWith('/api/harris-categories')) {
        return json(categories());
      }
      if (url === `/api/initiatives/${INITIATIVE_ID}` && method === 'GET') {
        return record === null ? Promise.resolve(new Response('', { status: 404 })) : json(record);
      }
      if (url === `/api/initiatives/${INITIATIVE_ID}/updates` && method === 'POST') {
        const body = JSON.parse(String(init?.body));
        if (!['OnTrack', 'AtRisk', 'OffTrack'].includes(body.ragStatus)) {
          return Promise.resolve(new Response('', { status: 422 }));
        }
        return json(
          {
            id: 'u-new',
            ragStatus: body.ragStatus,
            note: body.note ?? null,
            createdBy: 'Current User',
            createdAt: new Date().toISOString(),
          },
          201,
        );
      }
      if (url === `/api/initiatives/${INITIATIVE_ID}/nr-lines` && method === 'POST') {
        const body = JSON.parse(String(init?.body));
        return json({ id: 'nr-new', locked: false, description: null, ...body }, 201);
      }
      const deleteMatch = url.match(new RegExp(`^/api/initiatives/${INITIATIVE_ID}/nr-lines/(.+)$`));
      if (deleteMatch && method === 'DELETE') {
        const lineId = deleteMatch[1];
        const status = options.onDeleteNrLine ? options.onDeleteNrLine(lineId) : 204;
        // A 204 (No Content) response must have a null body — an empty string body throws in the
        // native Response constructor (undici/WHATWG fetch), unlike other empty-body statuses.
        return Promise.resolve(new Response(status === 204 ? null : '', { status }));
      }
      return Promise.resolve(new Response('', { status: 404 }));
    }),
  );
  return { calls };
}

describe('RegisterDetailScreen (HAP-14; register-detail mockup)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('renders loading then all cards on the happy path', async () => {
    installFetchMock();
    render(<RegisterDetailScreen initiativeId={INITIATIVE_ID} onBack={vi.fn()} />);

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Fictional Copilot Initiative' }),
    ).toBeTruthy();

    expect(screen.getByText(strings.registerDetail.nrTitle)).toBeTruthy();
    expect(screen.getByText(strings.registerDetail.governanceTitle)).toBeTruthy();
    expect(screen.getByText(strings.registerDetail.updateTitle)).toBeTruthy();
    expect(screen.getByText(strings.registerDetail.stageHistoryTitle)).toBeTruthy();
    expect(screen.getByText(strings.registerDetail.recentUpdatesTitle)).toBeTruthy();
    // Governance rows carry the label text (never colour-only).
    expect(screen.getByText('PHI')).toBeTruthy();
    expect(screen.getByText('Human-in-the-loop')).toBeTruthy();
  });

  it('renders a not-found state on a 404', async () => {
    installFetchMock({ detail: null });
    const onBack = vi.fn();
    render(<RegisterDetailScreen initiativeId={INITIATIVE_ID} onBack={onBack} />);

    expect(await screen.findByText(strings.registerDetail.notFoundTitle)).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: strings.registerDetail.backButton }));
    expect(onBack).toHaveBeenCalledTimes(1);
  });

  it('shows the overdue callout with the day count when stale beyond 7 days', async () => {
    installFetchMock({ detail: detail({ lastUpdateAt: daysAgoIso(11) }) });
    render(<RegisterDetailScreen initiativeId={INITIATIVE_ID} onBack={vi.fn()} />);
    await screen.findByRole('heading', { level: 1, name: 'Fictional Copilot Initiative' });

    const callout = await screen.findByRole('alert');
    expect(callout.textContent).toContain('11');
    expect(callout.textContent?.toLowerCase()).toContain('escalates to bu lead');
  });

  it('does NOT show the overdue callout for an Idea-stage initiative, even when stale beyond 7 days', async () => {
    installFetchMock({ detail: detail({ currentStage: 'Idea', lastUpdateAt: daysAgoIso(11) }) });
    render(<RegisterDetailScreen initiativeId={INITIATIVE_ID} onBack={vi.fn()} />);
    await screen.findByRole('heading', { level: 1, name: 'Fictional Copilot Initiative' });

    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('does NOT show the overdue callout for a Retired initiative, even when stale beyond 7 days', async () => {
    installFetchMock({ detail: detail({ currentStage: 'Retired', lastUpdateAt: daysAgoIso(11) }) });
    render(<RegisterDetailScreen initiativeId={INITIATIVE_ID} onBack={vi.fn()} />);
    await screen.findByRole('heading', { level: 1, name: 'Fictional Copilot Initiative' });

    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('renders the red RAG variant with its label text always printed (never colour-only)', async () => {
    installFetchMock({ detail: detail({ ragStatus: 'OffTrack' }) });
    const { container } = render(<RegisterDetailScreen initiativeId={INITIATIVE_ID} onBack={vi.fn()} />);
    await screen.findByRole('heading', { level: 1, name: 'Fictional Copilot Initiative' });

    // Scope to the identity card's chip row to avoid also matching the composer's "Off Track" radio
    // option label (same text, different element/purpose).
    const chip = container.querySelector('.register-detail-chips .rag-chip')!;
    expect(chip).toBeTruthy();
    expect(chip.className).toContain('rag-chip-red');
    expect(chip.textContent).toBe(strings.register.ragLabels.OffTrack);
  });

  it('adds an NR line on the happy path', async () => {
    installFetchMock();
    render(<RegisterDetailScreen initiativeId={INITIATIVE_ID} onBack={vi.fn()} />);
    await screen.findByRole('heading', { level: 1, name: 'Fictional Copilot Initiative' });

    fireEvent.change(screen.getByLabelText(strings.registerDetail.nrAmountLabel), {
      target: { value: '5000' },
    });
    fireEvent.change(screen.getByLabelText(strings.registerDetail.nrDescriptionLabel), {
      target: { value: 'New fictional NR line' },
    });
    fireEvent.click(screen.getByRole('button', { name: strings.registerDetail.nrAddLine }));

    await waitFor(() => expect(screen.getByText('New fictional NR line')).toBeTruthy());
  });

  it('deletes an unlocked NR line on the happy path, and surfaces an inline message for a locked (409) row', async () => {
    installFetchMock({
      // nr-3 is unlocked as far as the client's already-loaded view is concerned (it has a delete
      // button), but the server now rejects the delete with 409 — e.g. it became submission-referenced
      // after the page loaded. That must surface an inline message, not a thrown/uncaught error.
      onDeleteNrLine: (lineId) => (lineId === 'nr-3' ? 409 : 204),
    });
    render(<RegisterDetailScreen initiativeId={INITIATIVE_ID} onBack={vi.fn()} />);
    await screen.findByRole('heading', { level: 1, name: 'Fictional Copilot Initiative' });

    // Locked row (nr-2) shows a labelled lock indicator, not a delete button.
    expect(screen.getByText(strings.registerDetail.nrLockedLabel)).toBeTruthy();

    // Unlocked row (nr-1) has a delete button; deleting it succeeds (204) and removes the row.
    const deleteButtonFor = (description: string): HTMLElement =>
      screen.getByText(description).closest('tr')!.querySelector('.nr-delete-btn') as HTMLElement;

    fireEvent.click(deleteButtonFor('Fictional NR line one'));
    await waitFor(() => expect(screen.queryByText('Fictional NR line one')).toBeNull());
    // The already-locked row is untouched.
    expect(screen.getByText('Fictional NR line two (locked)')).toBeTruthy();

    // nr-3's delete is rejected (409) by the server — the row stays, and an inline message appears
    // instead of an uncaught error.
    fireEvent.click(deleteButtonFor('Fictional NR line three (locked server-side after load)'));
    expect(await screen.findByText(strings.registerDetail.nrDeleteLockedError)).toBeTruthy();
    expect(screen.getByText('Fictional NR line three (locked server-side after load)')).toBeTruthy();
  });

  it('posts a weekly update on the happy path and refreshes the update trail newest-first', async () => {
    installFetchMock();
    render(<RegisterDetailScreen initiativeId={INITIATIVE_ID} onBack={vi.fn()} />);
    await screen.findByRole('heading', { level: 1, name: 'Fictional Copilot Initiative' });

    fireEvent.click(
      screen.getByRole('radio', { name: strings.register.ragLabels.OffTrack }),
    );
    fireEvent.change(screen.getByLabelText(strings.registerDetail.noteLabel), {
      target: { value: 'Fresh status note' },
    });
    fireEvent.click(screen.getByRole('button', { name: strings.registerDetail.postUpdateButton }));

    expect(await screen.findByText(strings.registerDetail.updateSuccess)).toBeTruthy();

    const feed = screen.getByText(strings.registerDetail.recentUpdatesTitle).closest('section')!;
    const feedItems = within(feed).getAllByRole('listitem');
    // Newest (just-posted) note is the first item in the feed; the two seeded updates follow.
    expect(feedItems).toHaveLength(3);
    expect(feedItems[0].textContent).toContain('Fresh status note');
    expect(feedItems[1].textContent).toContain('Latest update note');
    expect(feedItems[2].textContent).toContain('Earlier update note');
  });

  it('hides/disables write controls when canEdit is false, without letting a request 404 silently', async () => {
    installFetchMock({ detail: detail({ canEdit: false }) });
    render(<RegisterDetailScreen initiativeId={INITIATIVE_ID} onBack={vi.fn()} />);
    await screen.findByRole('heading', { level: 1, name: 'Fictional Copilot Initiative' });

    expect(screen.queryByRole('button', { name: strings.registerDetail.postUpdateButton })).toBeNull();
    expect(screen.queryByRole('button', { name: strings.registerDetail.nrAddLine })).toBeNull();
    expect(screen.queryAllByRole('button', { name: strings.registerDetail.nrDeleteLabel })).toHaveLength(0);
    expect(screen.getAllByText(strings.registerDetail.notEditableNote).length).toBeGreaterThan(0);
  });

  it('hides the customers-in-production field when the category is not customer-deployed', async () => {
    installFetchMock({ detail: detail({ categoryCustomerDeployed: false, customersInProduction: null }) });
    render(<RegisterDetailScreen initiativeId={INITIATIVE_ID} onBack={vi.fn()} />);
    await screen.findByRole('heading', { level: 1, name: 'Fictional Copilot Initiative' });

    expect(screen.queryByLabelText(strings.registerDetail.customersFieldLabel)).toBeNull();
  });

  it('renders stage history in order with the current stage visually distinguished via a class', async () => {
    installFetchMock();
    render(<RegisterDetailScreen initiativeId={INITIATIVE_ID} onBack={vi.fn()} />);
    await screen.findByRole('heading', { level: 1, name: 'Fictional Copilot Initiative' });

    const items = screen.getAllByRole('listitem').filter((li) => li.className.includes('stage-timeline-item'));
    expect(items).toHaveLength(3);
    expect(items[0].textContent).toContain(strings.register.stageLabels.Idea);
    expect(items[1].textContent).toContain(strings.register.stageLabels.Evaluation);
    expect(items[2].textContent).toContain(strings.register.stageLabels.Pilot);
    expect(items[2].className).toContain('stage-timeline-item-current');
    expect(items[0].className).not.toContain('stage-timeline-item-current');
  });

  it('has no detectable accessibility violations', async () => {
    installFetchMock();
    const { container } = render(<RegisterDetailScreen initiativeId={INITIATIVE_ID} onBack={vi.fn()} />);
    await screen.findByRole('heading', { level: 1, name: 'Fictional Copilot Initiative' });

    const results = await axe(container, { rules: { 'color-contrast': { enabled: false } } });
    expect(results.violations).toEqual([]);
  });
});
