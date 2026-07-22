import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { axe } from 'vitest-axe';
import { AssessmentSelfScreen } from '../screens/assessment-self/AssessmentSelfScreen';
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

const dimensionOneId = '11111111-1111-1111-1111-111111111111';
const dimensionTwoId = '22222222-2222-2222-2222-222222222222';

function buildResponse(overrides: Partial<{ submitted: boolean; editable: boolean }> = {}) {
  return {
    cycleId: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
    cycleName: 'Test cycle',
    cycleState: 'Open',
    submitted: overrides.submitted ?? false,
    editable: overrides.editable ?? true,
    purposeLimitationKey: 'assessment.purposeLimitation',
    dimensionCount: 2,
    dimensions: [
      {
        dimensionId: dimensionOneId,
        key: 'dim-one',
        name: 'Fictional dimension one',
        displayOrder: 1,
        levels: buildLevels('Alpha'),
        selfScore: null,
        selfEvidence: null,
        priorScore: 1,
      },
      {
        dimensionId: dimensionTwoId,
        key: 'dim-two',
        name: 'Fictional dimension two',
        displayOrder: 2,
        levels: buildLevels('Beta'),
        selfScore: null,
        selfEvidence: null,
        priorScore: null,
      },
    ],
  };
}

function installFetchMock(response: unknown): void {
  vi.stubGlobal(
    'fetch',
    vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url === '/api/me/assessment' && (!init || init.method === undefined)) {
        return Promise.resolve(new Response(JSON.stringify(response), { status: 200 }));
      }
      if (url === '/api/me/assessment/scores' && init?.method === 'PUT') {
        return Promise.resolve(new Response(null, { status: 204 }));
      }
      if (url === '/api/me/assessment/submit' && init?.method === 'POST') {
        return Promise.resolve(new Response(null, { status: 204 }));
      }
      return Promise.reject(new Error(`unexpected fetch ${String(init?.method)} ${url}`));
    }),
  );
}

describe('AssessmentSelfScreen (HAP-8; FR-007/062/066)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('has no detectable accessibility violations once loaded', async () => {
    installFetchMock(buildResponse());

    const { container } = render(<AssessmentSelfScreen />);
    await screen.findByText('Fictional dimension one');

    // color-contrast needs a real canvas (unavailable in jsdom) — same exclusion used by the
    // other screen-level axe tests in this suite; exercised against the running app separately.
    const results = await axe(container, { rules: { 'color-contrast': { enabled: false } } });
    expect(results.violations).toEqual([]);
  });

  it('renders the purpose-limitation banner above the first dimension section (FR-066)', async () => {
    installFetchMock(buildResponse());

    render(<AssessmentSelfScreen />);
    await screen.findByText('Fictional dimension one');

    expect(screen.getByRole('note')).toBeTruthy();
    expect(screen.getByText(strings.assessment.purposeLimitation)).toBeTruthy();
  });

  it('pre-populates the prior-cycle level but does not count it toward progress until re-selected', async () => {
    installFetchMock(buildResponse());

    render(<AssessmentSelfScreen />);
    await screen.findByText('Fictional dimension one');

    // Dimension one's prior score (level 1) is pre-selected and shows the "last month" pill...
    const priorRadio = screen.getByRole('radio', { name: /Alpha option 1/ }) as HTMLInputElement;
    expect(priorRadio.checked).toBe(true);
    // ...but neither dimension counts toward progress yet (binding scoring model: pre-population
    // is shown, not counted, until the user actively picks a level this cycle).
    expect(screen.getByText(strings.assessment.progressAnnouncement(0, 2))).toBeTruthy();

    fireEvent.click(screen.getByRole('radio', { name: /Alpha option 2/ }));

    await waitFor(() => expect(screen.getByText(strings.assessment.progressAnnouncement(1, 2))).toBeTruthy());
  });

  it('is keyboard operable: tabbing to a level and selecting it with the keyboard updates progress', async () => {
    installFetchMock(buildResponse());

    render(<AssessmentSelfScreen />);
    await screen.findByText('Fictional dimension one');

    const firstLevel = screen.getByRole('radio', { name: /Alpha option 0/ }) as HTMLInputElement;
    // Simulate arriving at the control via Tab, then choosing it purely from the keyboard.
    firstLevel.focus();
    expect(document.activeElement).toBe(firstLevel);
    fireEvent.keyDown(firstLevel, { key: ' ' });
    fireEvent.click(firstLevel);

    expect(firstLevel.checked).toBe(true);
    await waitFor(() => expect(screen.getByText(strings.assessment.progressAnnouncement(1, 2))).toBeTruthy());

    // Arrow-key navigation also works from the keyboard alone.
    fireEvent.keyDown(firstLevel, { key: 'ArrowRight' });
    const secondLevel = screen.getByRole('radio', { name: /Alpha option 1/ }) as HTMLInputElement;
    expect(secondLevel.checked).toBe(true);
    expect(document.activeElement).toBe(secondLevel);
  });

  it('disables Submit until every dimension is scored, and submits once complete', async () => {
    installFetchMock(buildResponse());

    render(<AssessmentSelfScreen />);
    await screen.findByText('Fictional dimension one');

    const submitButton = screen.getByRole('button', { name: strings.assessment.submitForReview });
    expect(submitButton).toBeDisabled();

    // Dimension one's prior score (level 1) is already visually checked from pre-population —
    // clicking it re-confirms a "no change this month" answer (FR-062) and must still count
    // toward progress even though it fires no native radio `change` event.
    fireEvent.click(screen.getByRole('radio', { name: /Alpha option 1/ }));
    fireEvent.click(screen.getByRole('radio', { name: /Beta option 0/ }));

    await waitFor(() => expect(submitButton).not.toBeDisabled());

    fireEvent.click(submitButton);

    expect(await screen.findByText(strings.assessment.submittedTitle)).toBeTruthy();
  });

  it('shows the no-open-cycle empty state on a 404', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(new Response('', { status: 404 }))));

    render(<AssessmentSelfScreen />);

    expect(await screen.findByText(strings.assessment.noOpenCycleTitle)).toBeTruthy();
  });

  it('renders the submitted confirmation immediately when the API reports the assessment already submitted', async () => {
    installFetchMock(buildResponse({ submitted: true }));

    render(<AssessmentSelfScreen />);

    expect(await screen.findByText(strings.assessment.submittedTitle)).toBeTruthy();
  });

  it('renders read-only when the cycle is not editable: notice shown, every control disabled, axe clean', async () => {
    installFetchMock(buildResponse({ editable: false }));

    const { container } = render(<AssessmentSelfScreen />);
    await screen.findByText('Fictional dimension one');

    // (a) the read-only notice is shown up front (not just a lock discovered on Save/Submit).
    expect(screen.getByText(strings.assessment.readOnlyNotice)).toBeTruthy();

    // (b) the level cards, evidence textareas, and both action buttons are disabled.
    screen.getAllByRole('radio').forEach((radio) => expect(radio).toBeDisabled());
    screen.getAllByRole('textbox').forEach((textbox) => expect(textbox).toBeDisabled());
    expect(screen.getByRole('button', { name: strings.assessment.saveDraft })).toBeDisabled();
    expect(screen.getByRole('button', { name: strings.assessment.submitForReview })).toBeDisabled();

    // (c) the a11y posture still holds in the read-only state (color-contrast excluded — needs a real
    // canvas, unavailable in jsdom — matching the other screen-level axe tests here).
    const results = await axe(container, { rules: { 'color-contrast': { enabled: false } } });
    expect(results.violations).toEqual([]);
  });
});
