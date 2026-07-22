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

/**
 * Manager moderation endpoints (HAP-9; contracts/api.md "Manager scope"). Types mirror the API's
 * camelCase response shapes exactly. The visibility seam enforces chain scope server-side — the
 * client only ever passes the personId/assessmentId the caller was already shown in their own
 * review queue.
 */

export interface TeamReviewItem {
  /** Null when the report has no assessment row yet — the queue item is shown but not selectable. */
  assessmentId: string | null;
  personId: string;
  displayName: string;
  onLeave: boolean;
  state: 'NotStarted' | 'InProgress' | 'Submitted' | 'Moderated' | 'AutoAdopted';
  /** True only when Submitted and the cycle currently accepts moderation. */
  canModerate: boolean;
}

export interface TeamReviewsResponse {
  cycleId: string;
  cycleName: string;
  isManager: boolean;
  /** Empty when isManager is false. */
  reviews: TeamReviewItem[];
}

export interface MemberLevelResponse {
  level: number;
  levelName: string;
  descriptorText: string;
}

export interface MemberDimensionResponse {
  dimensionId: string;
  key: string;
  name: string;
  displayOrder: number;
  levels: MemberLevelResponse[];
  selfScore: number;
  selfEvidence: string | null;
  priorSelfScore: number | null;
  priorManagerScore: number | null;
  /** Existing moderated value — null until this dimension has been moderated. */
  managerScore: number | null;
  managerComment: string | null;
  /** FR-063: carry-forward if prior moderated & self unchanged, else adopt self. */
  defaultManagerScore: number;
  /** True when the carry-forward/adopt DEFAULT itself diverges at or beyond the sibling
   * `MemberAssessmentResponse.commentThreshold` from `selfScore` (a sustained prior forced-comment
   * moderation) — FR-009's forced-comment rule applies even before the manager touches this
   * dimension, since accepting the default as-is would otherwise 422 on submit. */
  defaultCommentRequired: boolean;
}

export interface MemberAssessmentResponse {
  assessmentId: string;
  personId: string;
  displayName: string;
  cycleId: string;
  cycleName: string;
  state: string;
  onLeave: boolean;
  /** Submitted AND the cycle accepts moderation (submission lock). */
  editable: boolean;
  /** FR-009's forced-comment divergence threshold (domain constant — server-owned, never a client
   * literal). A dimension's |self - manager| at or beyond this value requires a comment. */
  commentThreshold: number;
  dimensions: MemberDimensionResponse[];
}

export interface ModerationDecision {
  dimensionId: string;
  managerScore: number;
  comment: string | null;
}

/** GET /api/team/reviews — the caller's review queue. */
export async function fetchTeamReviews(): Promise<TeamReviewsResponse> {
  const response = await fetch('/api/team/reviews');
  if (!response.ok) {
    throw new Error(`GET /api/team/reviews failed (${response.status})`);
  }
  return (await response.json()) as TeamReviewsResponse;
}

/** GET /api/team/members/{personId}/assessment. Returns null on a 404 (not the caller's direct
 * report, or no assessment row for them) — an expected steady state, not an error. */
export async function fetchTeamMemberAssessment(personId: string): Promise<MemberAssessmentResponse | null> {
  const response = await fetch(`/api/team/members/${personId}/assessment`);
  if (response.status === 404) {
    return null;
  }
  if (!response.ok) {
    throw new Error(`GET /api/team/members/${personId}/assessment failed (${response.status})`);
  }
  return (await response.json()) as MemberAssessmentResponse;
}

/** PUT /api/team/reviews/{assessmentId} — the moderation write; transitions Submitted -> Moderated
 * on success. Dimensions omitted from `decisions` are defaulted server-side (carry-forward/adopt). */
export async function submitModeration(assessmentId: string, decisions: ModerationDecision[]): Promise<void> {
  const response = await fetch(`/api/team/reviews/${assessmentId}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ decisions }),
  });
  if (response.status !== 204) {
    throw new AssessmentWriteError(response.status);
  }
}

/**
 * Individual moderated-result endpoint (HAP-9; FR-012). Types mirror
 * `Hap.Api.AssessmentResultResponse` (System.Text.Json camelCase).
 */

export interface ResultDimensionResponse {
  dimensionId: string;
  key: string;
  name: string;
  displayOrder: number;
  levels: MemberLevelResponse[];
  selfScore: number;
  managerScore: number;
  managerComment: string | null;
  /** |self - manager|, computed server-side. */
  divergence: number;
}

export interface AssessmentResultResponse {
  cycleId: string;
  cycleName: string;
  state: string;
  moderatedAt: string | null;
  dimensions: ResultDimensionResponse[];
}

/** GET /api/me/assessment/result. Returns null on a 404 (not yet moderated) rather than throwing —
 * the self screen keeps its existing "submitted, awaiting moderation" state in that case. */
export async function fetchAssessmentResult(): Promise<AssessmentResultResponse | null> {
  const response = await fetch('/api/me/assessment/result');
  if (response.status === 404) {
    return null;
  }
  if (!response.ok) {
    throw new Error(`GET /api/me/assessment/result failed (${response.status})`);
  }
  return (await response.json()) as AssessmentResultResponse;
}
