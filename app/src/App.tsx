import { useCallback, useEffect, useState } from 'react';
import { fetchMe } from './api/client';
import { AppShell } from './shell/AppShell';
import { SignInScreen } from './screens/signin/SignInScreen';

type SessionState = 'checking' | 'anonymous' | 'authenticated';

/** Gates the app frame behind the local dev provider's session (FR-055): an unauthenticated
 * visitor sees the sign-in role picker; a signed-in one sees the application shell. */
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

  return <AppShell />;
}
