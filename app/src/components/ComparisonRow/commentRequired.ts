/**
 * FR-009's forced-comment rule for one dimension: required whenever the *current effective*
 * manager score diverges from the self score by at least `commentThreshold` — whether that score
 * is a manager edit or the untouched carry-forward/adopt default. `commentThreshold` is a
 * server-owned domain constant (`MemberAssessmentResponse.commentThreshold`) — it is never
 * hard-coded on the client, so this rule can't silently drift out of sync with the domain's actual
 * threshold. While the score still sits at the default, the server's own `defaultCommentRequired`
 * is also honoured (defensive, server-trusted signal) so GET and PUT never disagree; this can only
 * ever strengthen the rule, never weaken it. Kept in its own module (not re-exported alongside the
 * `ComparisonRow` component) so both `ComparisonRow` and `AssessmentModerationScreen`'s submit gate
 * share the exact same rule — a single source of truth against exactly the kind of drift a
 * "one-click accept all defaults" flow could otherwise silently 422 on.
 */
export function isCommentRequired(
  selfScore: number,
  managerScore: number,
  commentThreshold: number,
  defaultManagerScore?: number,
  defaultCommentRequired?: boolean,
): boolean {
  if (Math.abs(selfScore - managerScore) >= commentThreshold) {
    return true;
  }
  return defaultCommentRequired === true && defaultManagerScore !== undefined && managerScore === defaultManagerScore;
}
