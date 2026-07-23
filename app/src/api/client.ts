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
  /** True when this cycle's raw scores were destroyed under the GDPR retention policy (FR-052) — the
   * form shows an "erased" notice and no genuine current/prior values (optional for back-compat; the
   * server always sends it). */
  dataErased?: boolean;
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
  /** Null when this dimension was erased under retention (FR-052) — the `dataErased` branch renders an
   * erased notice instead of the score badges, so a null selfScore/managerScore is never read here. */
  selfScore: number | null;
  managerScore: number | null;
  managerComment: string | null;
  /** |self - manager|, computed server-side (0 when erased). */
  divergence: number;
  /** True when this dimension's scores were erased under retention (FR-052) — render an erased state,
   * not the placeholder score (optional for back-compat; the server always sends it). */
  erased?: boolean;
}

export interface AssessmentResultResponse {
  cycleId: string;
  cycleName: string;
  state: string;
  moderatedAt: string | null;
  /** True when this cycle's raw scores were destroyed under the GDPR retention policy (FR-052) — the
   * client renders an erased state rather than the placeholder scores. */
  dataErased?: boolean;
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

/**
 * Aggregate rollup endpoints (HAP-11; contracts/api.md [S] "BU Lead scope", "Group/Portfolio/HIG
 * Executive scope", own-team summary). Every response is suppression-projected server-side: a
 * suppressed node carries `suppressed: true` + a reason and `figures: null` — the client never
 * receives a number for a suppressed aggregate (F2 / FR-071). Types mirror
 * `Hap.Api.NodeAggregateResponse` (System.Text.Json camelCase).
 */

export interface AggregateFigures {
  n: number;
  /** Per-dimension mean of the moderated score of record (FR-015), keyed by dimension key. */
  perDimensionMean: Record<string, number>;
  /** Count of people at each floor level 0–3 (FR-016/018), keyed by the level as a string. */
  floorLevelDistribution: Record<string, number>;
  completionPct: number;
  unmoderatedPct: number;
}

export interface DimensionMeta {
  key: string;
  name: string;
  displayOrder: number;
}

export interface TrendPoint {
  cycleId: string;
  cycleName: string;
  suppressed: boolean;
  suppressionReason: string | null;
  /** Null when that cycle's node was suppressed. */
  figures: AggregateFigures | null;
}

export interface NodeAggregate {
  nodeType: string;
  nodeRef: string | null;
  nodeName: string;
  cycleId: string;
  cycleName: string;
  cycleState: string;
  /** True when computed live from an open cycle; false when read from a frozen snapshot. */
  live: boolean;
  suppressed: boolean;
  suppressionReason: string | null;
  /** Null when the node is suppressed — no number is ever sent for a suppressed aggregate. */
  figures: AggregateFigures | null;
  dimensions: DimensionMeta[];
  trend: TrendPoint[];
  /** FR-041 cross-module initiative counts — a null stub until HAP-13 ships the register. */
  initiatives: null;
}

/** GET /api/me/dashboard — the caller's DEFAULT dashboard node (BB2): the richest scope they lead
 * (Exec → all-HIG, Portfolio/Group Leader → their node, BU Lead → their BU, else → own team), resolved
 * and scope-checked server-side. Returns null on a 404 (no open cycle, or nothing to show). This is the
 * one call the router-less shell makes so each persona reaches their scope. */
export async function fetchDashboard(): Promise<NodeAggregate | null> {
  const response = await fetch('/api/me/dashboard');
  if (response.status === 404) {
    return null;
  }
  if (!response.ok) {
    throw new Error(`GET /api/me/dashboard failed (${response.status})`);
  }
  return (await response.json()) as NodeAggregate;
}

/** GET /api/me/team/summary — the caller's own team aggregate ([S], self-scoped, no id). Returns null
 * on a 404 (no open cycle, or the caller has no team of their own) rather than throwing. */
export async function fetchTeamSummary(): Promise<NodeAggregate | null> {
  const response = await fetch('/api/me/team/summary');
  if (response.status === 404) {
    return null;
  }
  if (!response.ok) {
    throw new Error(`GET /api/me/team/summary failed (${response.status})`);
  }
  return (await response.json()) as NodeAggregate;
}

/** GET /api/bus/{buId}/dashboard — the BU dashboard ([S]). Returns null on a 404 (out of the caller's
 * scope, or no open cycle). Available for an org-tree / BU-picker entry point (a later story). */
export async function fetchBuDashboard(buId: string): Promise<NodeAggregate | null> {
  const response = await fetch(`/api/bus/${buId}/dashboard`);
  if (response.status === 404) {
    return null;
  }
  if (!response.ok) {
    throw new Error(`GET /api/bus/${buId}/dashboard failed (${response.status})`);
  }
  return (await response.json()) as NodeAggregate;
}

/** GET /api/org/{nodeType}/{nodeId}/rollup (or /api/org/allhig/rollup) — Group/Portfolio/AllHig
 * aggregates only ([S]; FR-025). Returns null on a 404 (out of scope, or no open cycle). */
export async function fetchOrgRollup(nodeType: string, nodeId?: string): Promise<NodeAggregate | null> {
  const path =
    nodeType === 'allhig' || !nodeId ? '/api/org/allhig/rollup' : `/api/org/${nodeType}/${nodeId}/rollup`;
  const response = await fetch(path);
  if (response.status === 404) {
    return null;
  }
  if (!response.ok) {
    throw new Error(`GET ${path} failed (${response.status})`);
  }
  return (await response.json()) as NodeAggregate;
}

/**
 * Initiative register endpoints (HAP-13; contracts/api.md "Register"; FR-026/027/034/035). The register
 * is NOT visibility-seam-gated — it holds initiative data, not individual assessment data — so every
 * authenticated role may READ it. These read wrappers back the list UI; create/edit + the detail screen
 * are HAP-14. Types mirror `Hap.Api.RegisterEndpoints`' response records exactly (System.Text.Json
 * camelCase). The stage/RAG/risk unions mirror the backend enums (`InitiativeStage`, `HarrisStage`,
 * `RagStatus`, `RiskTier`) — internal state names, not framework/taxonomy content.
 */

export type InitiativeStage = 'Idea' | 'Evaluation' | 'Pilot' | 'Production' | 'Scaled' | 'Retired';
export type HarrisStage = 'Ideation' | 'Development' | 'Production' | 'IdeasTriedButStopped';
export type RagStatus = 'OnTrack' | 'AtRisk' | 'OffTrack';
export type RiskTier = 'Low' | 'Med' | 'High';

/** GET /api/business-units item — reference data for the BU filter/select. */
export interface BusinessUnitResponse {
  id: string;
  code: string;
  name: string;
}

/** GET /api/harris-categories item (FR-027) — reference data for the category filter/select. */
export interface HarrisCategoryResponse {
  id: string;
  key: string;
  name: string;
  groupReported: boolean;
  customerDeployed: boolean;
}

/** One register row (FR-026). `harrisStage` is the Harris-mapped label the list shows next to the
 * internal stage (Stage → Harris); null only if the stage map lacks a row (never in a seeded env). */
export interface InitiativeResponse {
  id: string;
  businessUnitId: string;
  name: string;
  description: string | null;
  sponsorPersonId: string | null;
  ownerPersonId: string;
  createdByPersonId: string;
  registeredAt: string;
  categoryId: string;
  aiDlcLevel: number;
  functionsAffected: string[];
  dimensionsAdvanced: string[];
  currentStage: InitiativeStage;
  harrisStage: HarrisStage | null;
  ragStatus: RagStatus;
  lastUpdateAt: string;
  customersInProduction: number | null;
  riskTier: RiskTier;
}

/** Server-side facets for GET /api/initiatives (FR-035). All optional; omitted params are not sent.
 * The mockup's RAG filter has no server param, so it is applied client-side in the list screen. */
export interface InitiativeQuery {
  search?: string;
  bu?: string;
  category?: string;
  stage?: string;
  riskTier?: string;
  aiDlcLevel?: number;
  dimension?: string;
}

/** GET /api/business-units — every authenticated role may read (non-seam-gated reference data). */
export async function fetchBusinessUnits(): Promise<BusinessUnitResponse[]> {
  const response = await fetch('/api/business-units');
  if (!response.ok) {
    throw new Error(`GET /api/business-units failed (${response.status})`);
  }
  return (await response.json()) as BusinessUnitResponse[];
}

/** GET /api/harris-categories — reference data for the register filters (FR-027). */
export async function fetchHarrisCategories(): Promise<HarrisCategoryResponse[]> {
  const response = await fetch('/api/harris-categories');
  if (!response.ok) {
    throw new Error(`GET /api/harris-categories failed (${response.status})`);
  }
  return (await response.json()) as HarrisCategoryResponse[];
}

/** GET /api/initiatives — full-text search + facets (FR-035). Results arrive sorted by last update
 * (newest first) server-side. Only the set facets are appended to the query string. */
export async function fetchInitiatives(query: InitiativeQuery = {}): Promise<InitiativeResponse[]> {
  const params = new URLSearchParams();
  if (query.search && query.search.trim()) {
    params.set('search', query.search.trim());
  }
  if (query.bu) {
    params.set('bu', query.bu);
  }
  if (query.category) {
    params.set('category', query.category);
  }
  if (query.stage) {
    params.set('stage', query.stage);
  }
  if (query.riskTier) {
    params.set('riskTier', query.riskTier);
  }
  if (query.aiDlcLevel != null) {
    params.set('aiDlcLevel', String(query.aiDlcLevel));
  }
  if (query.dimension) {
    params.set('dimension', query.dimension);
  }
  const qs = params.toString();
  const path = qs ? `/api/initiatives?${qs}` : '/api/initiatives';
  const response = await fetch(path);
  if (!response.ok) {
    throw new Error(`GET ${path} failed (${response.status})`);
  }
  return (await response.json()) as InitiativeResponse[];
}

/**
 * Initiative detail endpoints (HAP-14; FR-028). The detail screen (stage history, NR capture,
 * governance & risk, weekly-update composer) reads/writes through these. Types mirror the API's
 * camelCase response shapes exactly. `dataSensitivity`/`regulatoryRelevance`/etc. are the caller's
 * governance-and-risk fields (informational only, §4.2 — registering an initiative confers no
 * approval); `canEdit` is the caller's write authority for THIS initiative and gates every write
 * control the screen renders.
 */

export interface StageHistoryEntry {
  id: string;
  stage: InitiativeStage;
  priorStage: InitiativeStage | null;
  enteredAt: string;
  enteredBy: string;
}

export interface NrLine {
  id: string;
  year: number;
  direction: 'Direct' | 'Indirect';
  recurrence: 'OneTime' | 'Recurring';
  amountUsd: number;
  description: string | null;
  locked: boolean;
}

export interface WeeklyUpdate {
  id: string;
  ragStatus: RagStatus;
  note: string | null;
  createdBy: string;
  createdAt: string;
}

export interface InitiativeDetailResponse extends InitiativeResponse {
  dataSensitivity: 'None' | 'Internal' | 'PII' | 'PHI' | 'Clinical';
  regulatoryRelevance: string[];
  approvalStatus: string | null;
  approver: string | null;
  oversightModel: string | null;
  governanceNotes: string | null;
  modelsProviders: string[];
  vendorsTools: string[];
  usesCogito: boolean;
  /** Caller's write authority for THIS initiative — gate every write control on this. */
  canEdit: boolean;
  /** Whether "customers in production" is applicable for this initiative's Harris category. */
  categoryCustomerDeployed: boolean;
  /** Oldest -> newest. */
  stageHistory: StageHistoryEntry[];
  nrLines: NrLine[];
  /** Newest -> oldest. */
  updates: WeeklyUpdate[];
}

/** POST /api/initiatives/{id}/updates body. */
export interface WeeklyUpdateRequest {
  ragStatus: RagStatus;
  note?: string;
  customersInProduction?: number;
}

/** POST /api/initiatives/{id}/nr-lines body. */
export interface NrLineRequest {
  year: number;
  direction: 'Direct' | 'Indirect';
  recurrence: 'OneTime' | 'Recurring';
  amountUsd: number;
  description?: string;
}

/** Thrown by the initiative detail write calls with the response's HTTP status so the caller can map
 * it to the right inline message (404 no edit rights, 409 locked NR line, 422 bad input). */
export class InitiativeWriteError extends Error {
  readonly status: number;

  constructor(status: number) {
    super(`initiative write failed (${status})`);
    this.status = status;
  }
}

/** GET /api/initiatives/{id}. Returns null on a 404 (not found — this is a plain not-found state
 * for a register read, not an authorisation failure; the register is not seam-gated). */
export async function fetchInitiativeDetail(id: string): Promise<InitiativeDetailResponse | null> {
  const response = await fetch(`/api/initiatives/${id}`);
  if (response.status === 404) {
    return null;
  }
  if (!response.ok) {
    throw new Error(`GET /api/initiatives/${id} failed (${response.status})`);
  }
  return (await response.json()) as InitiativeDetailResponse;
}

/** POST /api/initiatives/{id}/updates — post a weekly RAG/note check-in. */
export async function postWeeklyUpdate(id: string, body: WeeklyUpdateRequest): Promise<WeeklyUpdate> {
  const response = await fetch(`/api/initiatives/${id}/updates`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  if (response.status !== 201) {
    throw new InitiativeWriteError(response.status);
  }
  return (await response.json()) as WeeklyUpdate;
}

/** POST /api/initiatives/{id}/nr-lines — add an NR capture line. */
export async function createNrLine(id: string, body: NrLineRequest): Promise<NrLine> {
  const response = await fetch(`/api/initiatives/${id}/nr-lines`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  if (response.status !== 201) {
    throw new InitiativeWriteError(response.status);
  }
  return (await response.json()) as NrLine;
}

/** DELETE /api/initiatives/{id}/nr-lines/{lineId} — 409 when the line is locked (submission-referenced). */
export async function deleteNrLine(id: string, lineId: string): Promise<void> {
  const response = await fetch(`/api/initiatives/${id}/nr-lines/${lineId}`, {
    method: 'DELETE',
  });
  if (response.status !== 204) {
    throw new InitiativeWriteError(response.status);
  }
}

/**
 * BU capture-forms endpoints (HAP-15; FR-047/FR-048; contracts/api.md "BU Lead scope" declarations
 * and metrics). The weekly AI-DLC declaration (level 0-3, RAG, next-level-expected date, optional note)
 * is shown beside the measured evidence panel — `BuDeclarationsResponse.measured` reuses HAP-11's
 * `NodeAggregate` shape verbatim (no new score query; the read is a rollup read, keeping this L2 not
 * L3, per the story's Phase-1 risk note). The monthly Support/SOR metrics form YTD-carries-forward the
 * customer-facing figures from the prior month while the SOR field is always current-month-only. Types
 * mirror the API's camelCase response shapes exactly. Both POSTs are UPSERTS — a same-week/same-month
 * resubmission updates the existing row, so the client always POSTs the full form rather than tracking
 * a separate PUT/id.
 */

export interface BuDeclarationResponse {
  id: string;
  businessUnitId: string;
  /** ISO date — the Monday of the declared week. */
  weekOf: string;
  declaredLevel: number;
  nextLevelExpectedDate: string | null;
  ragStatus: RagStatus;
  note: string | null;
  declaredBy: string;
  createdAt: string;
  updatedAt: string;
}

export interface BuDeclarationsResponse {
  businessUnitId: string;
  /** Newest week first. */
  declarations: BuDeclarationResponse[];
  /** The measured-evidence aggregate (HAP-11's rollup output, suppression-projected server-side). */
  measured: NodeAggregate;
  measuredFloorLevel: number | null;
  /** declaredLevel - measuredFloorLevel, signed. Null when either side is unavailable. */
  declaredVsMeasuredDivergence: number | null;
}

/** POST /api/bus/{buId}/declarations body — an upsert keyed on (businessUnitId, weekOf). */
export interface BuDeclarationRequest {
  weekOf: string;
  declaredLevel: number;
  nextLevelExpectedDate: string | null;
  ragStatus: RagStatus;
  note: string | null;
}

export interface SupportInternal {
  timeSavingsPct: number | null;
  fewerPeopleNeeded: string | null;
  supportRatioImpact: string | null;
}

export interface SupportCustomer {
  customersYtd: number | null;
  ticketsYtd: number | null;
  resolvedByAiYtd: number | null;
  aiAssistedYtd: number | null;
}

export interface BuMonthlyMetricsResponse {
  businessUnitId: string;
  /** ISO date, first-of-month. */
  month: string;
  supportInternal: SupportInternal;
  supportCustomer: SupportCustomer;
  sorCalledByOtherApps: string | null;
  /** True when `supportCustomer` was pre-filled from the PRIOR month (no row saved yet this month). */
  carriedForward: boolean;
  submittedBy: string | null;
  createdAt: string | null;
}

/** POST /api/bus/{buId}/metrics body — an upsert keyed on (businessUnitId, month). */
export interface BuMonthlyMetricsRequest {
  month: string;
  supportInternal: SupportInternal;
  supportCustomer: SupportCustomer;
  sorCalledByOtherApps: string | null;
}

/** Thrown by the BU capture-forms write calls with the response's HTTP status so the caller can map it
 * to the right inline message (403 not BU Lead/delegate, 422 validation, 404 unknown BU). */
export class BuFormsWriteError extends Error {
  readonly status: number;

  constructor(status: number) {
    super(`BU forms write failed (${status})`);
    this.status = status;
  }
}

/** GET /api/bus/{buId}/declarations. Returns null on a 404 (unknown BU, or out of the caller's scope)
 * rather than throwing — an expected steady state, not an error. */
export async function fetchBuDeclarations(buId: string): Promise<BuDeclarationsResponse | null> {
  const response = await fetch(`/api/bus/${buId}/declarations`);
  if (response.status === 404) {
    return null;
  }
  if (!response.ok) {
    throw new Error(`GET /api/bus/${buId}/declarations failed (${response.status})`);
  }
  return (await response.json()) as BuDeclarationsResponse;
}

/** POST /api/bus/{buId}/declarations — upserts the caller's declaration for `body.weekOf`. */
export async function postBuDeclaration(buId: string, body: BuDeclarationRequest): Promise<BuDeclarationResponse> {
  const response = await fetch(`/api/bus/${buId}/declarations`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  if (response.status !== 200 && response.status !== 201) {
    throw new BuFormsWriteError(response.status);
  }
  return (await response.json()) as BuDeclarationResponse;
}

/** GET /api/bus/{buId}/metrics?month=yyyy-MM-dd. Returns null on a 404 (unknown BU, or out of the
 * caller's scope) rather than throwing. `month` is an ISO date string, first-of-month. */
export async function fetchBuMetrics(buId: string, month: string): Promise<BuMonthlyMetricsResponse | null> {
  const response = await fetch(`/api/bus/${buId}/metrics?month=${encodeURIComponent(month)}`);
  if (response.status === 404) {
    return null;
  }
  if (!response.ok) {
    throw new Error(`GET /api/bus/${buId}/metrics failed (${response.status})`);
  }
  return (await response.json()) as BuMonthlyMetricsResponse;
}

/** POST /api/bus/{buId}/metrics — upserts the caller's monthly metrics for `body.month`. */
export async function postBuMetrics(buId: string, body: BuMonthlyMetricsRequest): Promise<BuMonthlyMetricsResponse> {
  const response = await fetch(`/api/bus/${buId}/metrics`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  if (response.status !== 200 && response.status !== 201) {
    throw new BuFormsWriteError(response.status);
  }
  return (await response.json()) as BuMonthlyMetricsResponse;
}
