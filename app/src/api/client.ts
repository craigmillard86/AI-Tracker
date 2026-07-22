/**
 * Thin fetch wrapper for the identity endpoints (FR-055). Uses relative URLs — the Vite dev-server
 * proxy (vite.config.ts) forwards /auth and /api to the API origin, so the browser only ever talks
 * to the app's own origin and the auth cookie never needs to cross one.
 */

/** One seeded role-picker option (contracts/api.md "Auth" — GET /auth/signin). Field names match
 * the API's snake_case wire shape (Hap.Api.Identity.SeedUserRecord). */
export interface SignInOption {
  role: string;
  external_ref: string;
  name: string;
  email: string;
  bu_code: string;
}

/** GET /api/me response shape (Hap.Api.Identity.MeResponse; System.Text.Json default camelCase). */
export interface MeResponse {
  personId: string;
  externalRef: string;
  displayName: string;
  email: string;
  jobTitle: string;
  businessUnitCode: string;
  explicitRoles: string[];
  computedRoles: string[];
  currentCycleStatus: string | null;
}

export async function fetchSignInOptions(): Promise<SignInOption[]> {
  const response = await fetch('/auth/signin');
  if (!response.ok) {
    throw new Error(`GET /auth/signin failed (${response.status})`);
  }
  return (await response.json()) as SignInOption[];
}

export async function signIn(userKey: string): Promise<void> {
  const response = await fetch('/auth/signin', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ userKey }),
  });
  if (!response.ok) {
    throw new Error(`POST /auth/signin failed (${response.status})`);
  }
}

export async function signOut(): Promise<void> {
  await fetch('/auth/signout', { method: 'POST' });
}

/** Returns null for an unauthenticated caller (401) rather than throwing — that is the
 * expected, common "not signed in yet" state, not an error. */
export async function fetchMe(): Promise<MeResponse | null> {
  const response = await fetch('/api/me');
  if (response.status === 401) {
    return null;
  }
  if (!response.ok) {
    throw new Error(`GET /api/me failed (${response.status})`);
  }
  return (await response.json()) as MeResponse;
}

/**
 * Self-assessment endpoints (HAP-8; contracts/api.md "Self scope"). Types mirror
 * `Hap.Api.SelfAssessmentResponse` et al. exactly (System.Text.Json camelCase) — the subject is
 * always the authenticated caller, so no personId is ever sent from the client.
 */

export interface SelfLevelResponse {
  level: number;
  levelName: string;
  descriptorText: string;
}

export interface SelfDimensionResponse {
  dimensionId: string;
  key: string;
  name: string;
  displayOrder: number;
  levels: SelfLevelResponse[];
  selfScore: number | null;
  selfEvidence: string | null;
  priorScore: number | null;
}

export interface SelfAssessmentResponse {
  cycleId: string;
  cycleName: string;
  cycleState: string;
  submitted: boolean;
  /** False when the cycle no longer accepts this caller's writes (closed without a late override) or
   * the assessment is already submitted — the form is rendered read-only rather than surfacing the
   * lock only on Save/Submit. */
  editable: boolean;
  purposeLimitationKey: string;
  dimensionCount: number;
  dimensions: SelfDimensionResponse[];
}

export interface ScoreEntry {
  dimensionId: string;
  score: number;
  evidence: string | null;
}

/** Thrown by the assessment write calls with the response's HTTP status so the caller can map it
 * to the right inline message (422 out-of-range/incomplete, 409 already submitted, 423 locked). */
export class AssessmentWriteError extends Error {
  readonly status: number;

  constructor(status: number) {
    super(`assessment write failed (${status})`);
    this.status = status;
  }
}

/** GET /api/me/assessment. Returns null for a 404 (no cycle currently open) rather than
 * throwing — that is an expected steady state the screen renders explicitly, not an error. */
export async function fetchSelfAssessment(): Promise<SelfAssessmentResponse | null> {
  const response = await fetch('/api/me/assessment');
  if (response.status === 404) {
    return null;
  }
  if (!response.ok) {
    throw new Error(`GET /api/me/assessment failed (${response.status})`);
  }
  return (await response.json()) as SelfAssessmentResponse;
}

/** PUT /api/me/assessment/scores — partial-progress upsert (save draft). */
export async function saveSelfAssessmentScores(scores: ScoreEntry[]): Promise<void> {
  const response = await fetch('/api/me/assessment/scores', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ scores }),
  });
  if (response.status !== 204) {
    throw new AssessmentWriteError(response.status);
  }
}

/** POST /api/me/assessment/submit — no body; subject and cycle are derived from the session. */
export async function submitSelfAssessment(): Promise<void> {
  const response = await fetch('/api/me/assessment/submit', { method: 'POST' });
  if (response.status !== 204) {
    throw new AssessmentWriteError(response.status);
  }
}
