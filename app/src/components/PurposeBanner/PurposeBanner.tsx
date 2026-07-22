import { strings } from '../../strings';

/**
 * GDPR purpose-limitation statement (FR-066; DESIGN.md A8). Static, non-dismissible — `role="note"`
 * so assistive tech announces it as supplementary but not interactive/alert content. The copy is a
 * fixed string in the strings module (FR-067); the API only ever sends the key
 * (`purposeLimitationKey`) that names it, never prose, so the wording lives in exactly one place.
 */
export function PurposeBanner(): JSX.Element {
  return (
    <div className="purpose-banner" role="note">
      <svg className="purpose-banner-icon" viewBox="0 0 20 20" aria-hidden="true" focusable="false">
        <circle cx="10" cy="10" r="9" fill="none" stroke="currentColor" strokeWidth="1.5" />
        <line x1="10" y1="9" x2="10" y2="14.5" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
        <circle cx="10" cy="6" r="1" fill="currentColor" />
      </svg>
      <p className="purpose-banner-text">{strings.assessment.purposeLimitation}</p>
    </div>
  );
}
