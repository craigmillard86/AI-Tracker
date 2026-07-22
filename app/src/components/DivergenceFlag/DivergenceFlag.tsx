import { strings } from '../../strings';

export interface DivergenceFlagProps {
  /** The individual's self score for this dimension. */
  selfScore: number;
  /** The manager's (current or moderated) score for this dimension. */
  managerScore: number;
}

/**
 * Divergence indicator between a self score and a manager score (DESIGN.md A8; FR-009/012). Used
 * by both `ComparisonRow` (moderation table) and the individual's moderated-result section. The
 * numeric divergence is always printed as text ("agree" or "Δ N") — colour is reinforcement only,
 * never the sole signal (DESIGN.md A5/A7).
 */
export function DivergenceFlag({ selfScore, managerScore }: DivergenceFlagProps): JSX.Element {
  const divergence = Math.abs(selfScore - managerScore);

  let variant: 'agree' | 'mid' | 'high';
  if (divergence === 0) {
    variant = 'agree';
  } else if (divergence === 1) {
    variant = 'mid';
  } else {
    variant = 'high';
  }

  return (
    <span className={`divergence-flag divergence-flag-${variant}`}>
      {variant === 'agree' ? strings.moderation.agreeChip : strings.moderation.divergenceValue(divergence)}
    </span>
  );
}
