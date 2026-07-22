namespace Hap.Domain.Assessments;

/// <summary>
/// The lifecycle of one person's assessment for one cycle (data-model.md "Assessment"; spec Key
/// Entities): <c>NotStarted → InProgress → Submitted → (Moderated | AutoAdopted)</c>. Forward-only.
/// This story (HAP-8, self-assessment) drives the first three states; <see cref="Moderated"/> is
/// HAP-9 (manager moderation) and <see cref="AutoAdopted"/> is HAP-10 (cycle-close auto-adoption,
/// FR-068).
/// </summary>
public enum AssessmentState
{
    NotStarted,
    InProgress,
    Submitted,
    Moderated,
    AutoAdopted,
}
