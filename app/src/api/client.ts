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
