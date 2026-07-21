import { useEffect, useState } from 'react';
import { fetchSignInOptions, signIn, type SignInOption } from '../../api/client';
import { strings } from '../../strings';

interface SignInScreenProps {
  /** Called once a sign-in attempt succeeds so the caller can re-check session state. */
  onSignedIn: () => void;
}

type LoadState =
  | { status: 'loading' }
  | { status: 'error' }
  | { status: 'ready'; options: SignInOption[] };

/**
 * The local dev provider's role picker (FR-055; research D3). No mockup exists for this screen
 * (QUESTIONS.md Q-004) — built minimal, DESIGN.md-conformant: cards + buttons only, tokens.css
 * only, no new colours/type sizes/components.
 */
export function SignInScreen({ onSignedIn }: SignInScreenProps): JSX.Element {
  const [state, setState] = useState<LoadState>({ status: 'loading' });
  const [signingInAs, setSigningInAs] = useState<string | null>(null);
  const [signInFailed, setSignInFailed] = useState(false);

  useEffect(() => {
    let cancelled = false;
    fetchSignInOptions()
      .then((options) => {
        if (!cancelled) {
          setState({ status: 'ready', options });
        }
      })
      .catch(() => {
        if (!cancelled) {
          setState({ status: 'error' });
        }
      });
    return () => {
      cancelled = true;
    };
  }, []);

  async function handleSignIn(externalRef: string): Promise<void> {
    setSigningInAs(externalRef);
    setSignInFailed(false);
    try {
      await signIn(externalRef);
      onSignedIn();
    } catch {
      setSignInFailed(true);
      setSigningInAs(null);
    }
  }

  return (
    <div className="signin-screen">
      <div className="signin-card-panel">
        <h1 className="signin-title">{strings.signIn.title}</h1>
        <p className="signin-subtitle">{strings.signIn.subtitle}</p>

        {state.status === 'loading' && <p className="signin-status">{strings.signIn.loading}</p>}
        {state.status === 'error' && (
          <p className="signin-status signin-status-error" role="alert">
            {strings.signIn.loadError}
          </p>
        )}
        {signInFailed && (
          <p className="signin-status signin-status-error" role="alert">
            {strings.signIn.signInError}
          </p>
        )}

        {state.status === 'ready' && (
          <ul className="signin-role-grid">
            {state.options.map((option) => (
              <li key={option.external_ref}>
                <button
                  type="button"
                  className="signin-role-card"
                  onClick={() => handleSignIn(option.external_ref)}
                  disabled={signingInAs !== null}
                  aria-label={`${strings.signIn.signInAsPrefix} ${option.name}, ${option.role}`}
                >
                  <span className="signin-role-label">{option.role}</span>
                  <span className="signin-role-name">{option.name}</span>
                  <span className="signin-role-bu">{option.bu_code}</span>
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}
