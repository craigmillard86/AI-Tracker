import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { axe } from 'vitest-axe';
import { AssessmentModerationScreen } from '../screens/assessment-moderation/AssessmentModerationScreen';
import { strings } from '../strings';

// Deliberately fictional dimension/level wording — Art. II.4 / the framework grep-guard forbids
// real framework content (dimension names, level names, descriptors) from appearing anywhere in
// app/src, tests included. All binding content in the real app comes from the API response.
function buildLevels(prefix: string): Array<{ level: number; levelName: string; descriptorText: string }> {
  return [0, 1, 2, 3].map((level) => ({
    level,
    levelName: `${prefix} option ${level}`,
    descriptorText: `${prefix} fictional descriptor ${level} for testing purposes only.`,
  }));
}

const dimAgreeId = '11111111-1111-1111-1111-111111111111';
const dimMidId = '22222222-2222-2222-2222-222222222222';
const dimHighId = '33333333-3333-3333-3333-333333333333';

const reviewsResponse = {
  cycleId: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
  cycleName: 'Fictional cycle FY99-M01',
  isManager: true,
  reviews: [
    {
      assessmentId: 'aaaaaaaa-0000-0000-0000-000000000001',
      personId: 'p1',
      displayName: 'Marcus Reed',
      onLeave: false,
      state: 'Moderated',
      canModerate: false,
    },
    {
      assessmentId: 'aaaaaaaa-0000-0000-0000-000000000002',
      personId: 'p2',
      displayName: 'Elena Farr',
      onLeave: false,
      state: 'Submitted',
      canModerate: true,
    },
    {
      assessmentId: null,
      personId: 'p3',
      displayName: 'Sofia Nunez',
      onLeave: false,
      state: 'NotStarted',
      canModerate: false,
    },
  ],
};

function memberAssessmentResponse() {
  return {
    assessmentId: 'aaaaaaaa-0000-0000-0000-000000000002',
    personId: 'p2',
    displayName: 'Elena Farr',
    cycleId: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
    cycleName: 'Fictional cycle FY99-M01',
    state: 'Submitted',
    onLeave: false,
    editable: true,
    // Server-owned FR-009 domain constant — never a client literal (panel follow-up).
    commentThreshold: 2,
    dimensions: [
      {
        dimensionId: dimAgreeId,
        key: 'dim-agree',
        name: 'Fictional dimension agree',
        displayOrder: 1,
        levels: buildLevels('Alpha'),
        selfScore: 2,
        selfEvidence: null,
        priorSelfScore: null,
        priorManagerScore: null,
        managerScore: null,
        managerComment: null,
        defaultManagerScore: 2,
        defaultCommentRequired: false,
      },
      {
        dimensionId: dimMidId,
        key: 'dim-mid',
        name: 'Fictional dimension mid',
        displayOrder: 2,
        levels: buildLevels('Beta'),
        selfScore: 1,
        selfEvidence: null,
        priorSelfScore: null,
        priorManagerScore: null,
        managerScore: null,
        managerComment: null,
        defaultManagerScore: 2,
        defaultCommentRequired: false,
      },
      {
        dimensionId: dimHighId,
        key: 'dim-high',
        name: 'Fictional dimension high',
        displayOrder: 3,
        levels: buildLevels('Gamma'),
        selfScore: 3,
        selfEvidence: null,
        priorSelfScore: null,
        priorManagerScore: null,
        managerScore: null,
        managerComment: null,
        // Sustained prior Δ>=2 moderation: the carry-forward default (L1) itself diverges from the
        // self score (L3) by 2 — the server flags this dimension as comment-required even before
        // any manager edit (FR-063/FR-009 consistency; panel follow-up).
        defaultManagerScore: 1,
        defaultCommentRequired: true,
      },
    ],
  };
}

function installFetchMock(): void {
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url === '/api/team/reviews' && (!init || init.method === undefined)) {
        return Promise.resolve(new Response(JSON.stringify(reviewsResponse), { status: 200 }));
      }
      if (url === '/api/team/members/p2/assessment') {
        return Promise.resolve(new Response(JSON.stringify(memberAssessmentResponse()), { status: 200 }));
      }
      if (url.startsWith('/api/team/reviews/') && init?.method === 'PUT') {
        return Promise.resolve(new Response(null, { status: 204 }));
      }
      return Promise.reject(new Error(`unexpected fetch ${String(init?.method)} ${url}`));
    }),
  );
}

/** Stateful variant that mirrors the server transitioning Elena Farr's assessment to
 * state=Moderated/editable=false once the PUT succeeds — so the post-submit refetch
 * (AssessmentModerationScreen's read-only-after-submit fix) has a real state change to reflect. */
function installStatefulFetchMock(): void {
  let moderated = false;
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url === '/api/team/reviews' && (!init || init.method === undefined)) {
        const reviews = moderated
          ? {
              ...reviewsResponse,
              reviews: reviewsResponse.reviews.map((item) =>
                item.personId === 'p2' ? { ...item, state: 'Moderated', canModerate: false } : item,
              ),
            }
          : reviewsResponse;
        return Promise.resolve(new Response(JSON.stringify(reviews), { status: 200 }));
      }
      if (url === '/api/team/members/p2/assessment') {
        const base = memberAssessmentResponse();
        const body = moderated
          ? {
              ...base,
              state: 'Moderated',
              editable: false,
              dimensions: base.dimensions.map((dimension) => ({
                ...dimension,
                managerScore: dimension.managerScore ?? dimension.defaultManagerScore,
                managerComment:
                  dimension.dimensionId === dimHighId
                    ? 'Evidence reviewed — moderating from L3 self-score to L1.'
                    : dimension.managerComment,
              })),
            }
          : base;
        return Promise.resolve(new Response(JSON.stringify(body), { status: 200 }));
      }
      if (url.startsWith('/api/team/reviews/') && init?.method === 'PUT') {
        moderated = true;
        return Promise.resolve(new Response(null, { status: 204 }));
      }
      return Promise.reject(new Error(`unexpected fetch ${String(init?.method)} ${url}`));
    }),
  );
}

describe('AssessmentModerationScreen (HAP-9; FR-008/009/010/011/063/069)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('auto-selects the first moderatable report and shows both self and manager scores', async () => {
    installFetchMock();

    render(<AssessmentModerationScreen />);

    // "Elena Farr" appears both as the review-queue entry and the card heading — the heading role
    // disambiguates the wait target.
    expect(await screen.findByRole('heading', { name: 'Elena Farr' })).toBeTruthy();
    expect(screen.getByText('Fictional dimension agree')).toBeTruthy();
  });

  it('pre-fills the manager score from defaultManagerScore (FR-063 carry-forward default)', async () => {
    installFetchMock();

    render(<AssessmentModerationScreen />);
    await screen.findByText('Fictional dimension agree');

    const agreeSelect = screen.getByLabelText(
      strings.moderation.yourScoreLabel('Fictional dimension agree'),
    ) as HTMLSelectElement;
    expect(agreeSelect.value).toBe('2');

    const highSelect = screen.getByLabelText(
      strings.moderation.yourScoreLabel('Fictional dimension high'),
    ) as HTMLSelectElement;
    expect(highSelect.value).toBe('1');
  });

  it('shows the "agree" chip at divergence 0 and a subtle flag at divergence 1', async () => {
    installFetchMock();

    render(<AssessmentModerationScreen />);
    await screen.findByText('Fictional dimension agree');

    expect(screen.getByText(strings.moderation.agreeChip)).toBeTruthy();
    expect(screen.getByText(strings.moderation.divergenceValue(1))).toBeTruthy();
  });

  it('forces a comment at divergence >= 2 and blocks submit until one is entered', async () => {
    installFetchMock();

    render(<AssessmentModerationScreen />);
    await screen.findByText('Fictional dimension high');

    expect(screen.getByText(strings.moderation.divergenceValue(2))).toBeTruthy();
    expect(screen.getByText(strings.moderation.commentRequiredError)).toBeTruthy();

    const submitButton = screen.getByRole('button', { name: strings.moderation.submitModeration });
    const saveAndNextButton = screen.getByRole('button', { name: strings.moderation.saveAndNext });
    expect(submitButton).toBeDisabled();
    expect(saveAndNextButton).toBeDisabled();

    const commentField = screen.getByLabelText(strings.moderation.commentLabel('Fictional dimension high'));
    fireEvent.change(commentField, {
      target: { value: 'Evidence reviewed — moderating from L3 self-score to L1.' },
    });

    await waitFor(() => expect(submitButton).not.toBeDisabled());
    expect(screen.queryByText(strings.moderation.commentRequiredError)).toBeFalsy();
  });

  it('submits the moderation decisions and shows a success confirmation', async () => {
    installFetchMock();

    render(<AssessmentModerationScreen />);
    await screen.findByText('Fictional dimension high');

    fireEvent.change(screen.getByLabelText(strings.moderation.commentLabel('Fictional dimension high')), {
      target: { value: 'Evidence reviewed — moderating from L3 self-score to L1.' },
    });

    const submitButton = await screen.findByRole('button', { name: strings.moderation.submitModeration });
    await waitFor(() => expect(submitButton).not.toBeDisabled());
    fireEvent.click(submitButton);

    expect(await screen.findByText(strings.moderation.moderationComplete)).toBeTruthy();
  });

  it('becomes read-only after a successful submit — score selects, comment fields, and both action buttons disable so a stray second click cannot fire against an already-moderated assessment', async () => {
    installStatefulFetchMock();

    render(<AssessmentModerationScreen />);
    await screen.findByText('Fictional dimension high');

    fireEvent.change(screen.getByLabelText(strings.moderation.commentLabel('Fictional dimension high')), {
      target: { value: 'Evidence reviewed — moderating from L3 self-score to L1.' },
    });

    const submitButton = await screen.findByRole('button', { name: strings.moderation.submitModeration });
    await waitFor(() => expect(submitButton).not.toBeDisabled());
    fireEvent.click(submitButton);

    expect(await screen.findByText(strings.moderation.moderationComplete)).toBeTruthy();

    // Every score select and comment textarea is now disabled...
    await waitFor(() => {
      screen.getAllByRole('combobox').forEach((select) => expect(select).toBeDisabled());
      screen.getAllByRole('textbox').forEach((textbox) => expect(textbox).toBeDisabled());
    });
    // ...and both action buttons are disabled, so a second click can't re-fire the write.
    expect(screen.getByRole('button', { name: strings.moderation.submitModeration })).toBeDisabled();
    expect(screen.getByRole('button', { name: strings.moderation.saveAndNext })).toBeDisabled();
    // The read-only notice is shown alongside the success confirmation.
    expect(screen.getByText(strings.moderation.readOnlyNotice)).toBeTruthy();
  });

  it('renders the review queue with done / reviewing / to-do states', async () => {
    installFetchMock();

    render(<AssessmentModerationScreen />);
    await screen.findByText('Fictional dimension agree');

    const queue = screen.getByText(strings.moderation.queueTitle).closest('.moderation-aside-panel') as HTMLElement;
    expect(within(queue).getByText('Marcus Reed')).toBeTruthy();
    expect(within(queue).getAllByText(strings.moderation.queueStateDone).length).toBeGreaterThan(0);
    expect(within(queue).getAllByText(strings.moderation.queueStateReviewing).length).toBeGreaterThan(0);
    expect(within(queue).getAllByText(strings.moderation.queueStateToDo).length).toBeGreaterThan(0);

    // Sofia Nunez has no assessment row yet (assessmentId null) — not selectable.
    const sofiaButton = within(queue).getByText('Sofia Nunez').closest('button') as HTMLButtonElement;
    expect(sofiaButton).toBeDisabled();
  });

  it('the manager-score select is keyboard operable (focus + value change updates the pre-filled score)', async () => {
    installFetchMock();

    render(<AssessmentModerationScreen />);
    await screen.findByText('Fictional dimension agree');

    const select = screen.getByLabelText(
      strings.moderation.yourScoreLabel('Fictional dimension agree'),
    ) as HTMLSelectElement;
    select.focus();
    expect(document.activeElement).toBe(select);

    fireEvent.change(select, { target: { value: '0' } });
    expect(select.value).toBe('0');
    // Changing the manager score away from self (2) now shows a divergence flag instead of "agree"
    // in this row — scoped to the row since the fixture's high-divergence row already shows its
    // own "Δ 2" flag by default.
    const row = select.closest('tr') as HTMLElement;
    expect(within(row).getByText(strings.moderation.divergenceValue(2))).toBeTruthy();
  });

  it('has no detectable accessibility violations once loaded', async () => {
    installFetchMock();

    const { container } = render(<AssessmentModerationScreen />);
    await screen.findByText('Fictional dimension agree');

    // color-contrast needs a real canvas (unavailable in jsdom) — same exclusion used by the other
    // screen-level axe tests in this suite.
    const results = await axe(container, { rules: { 'color-contrast': { enabled: false } } });
    expect(results.violations).toEqual([]);
  });

  it('shows the not-a-manager empty state when the caller has no direct reports', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() =>
        Promise.resolve(
          new Response(
            JSON.stringify({ cycleId: 'c1', cycleName: 'Fictional cycle', isManager: false, reviews: [] }),
            { status: 200 },
          ),
        ),
      ),
    );

    render(<AssessmentModerationScreen />);

    expect(await screen.findByText(strings.moderation.notManagerTitle)).toBeTruthy();
  });
});
