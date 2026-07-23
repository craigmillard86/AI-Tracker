import { useCallback, useEffect, useState } from 'react';
import { fetchMe } from './api/client';
import { AppShell } from './shell/AppShell';
import { SignInScreen } from './screens/signin/SignInScreen';

type SessionState = 'checking' | 'anonymous' | 'authenticated';

/** Gates the app frame behind the local dev provider's session (FR-055): an unauthenticated
 * visitor sees the sign-in role picker; a signed-in one sees the application shell. Sign-out
 * (HAP-23) is the reverse edge — AppShell calls back here once it has cleared the server-side
 * session, and setting `session` back to 'anonymous' unmounts AppShell itself, which is what
 * discards its locally-held identity rather than any explicit "clear" step. */
export function App(): JSX.Element {
  const [session, setSession] = useState<SessionState>('checking');

  const checkSession = useCallback(() => {
    fetchMe()
      .then((me) => setSession(me ? 'authenticated' : 'anonymous'))
      .catch(() => setSession('anonymous'));
  }, []);

  useEffect(() => {
    checkSession();
  }, [checkSession]);

  if (session === 'checking') {
    return <></>;
  }

  if (session === 'anonymous') {
    return <SignInScreen onSignedIn={checkSession} />;
  }

  return <AppShell onSignedOut={() => setSession('anonymous')} />;
}
