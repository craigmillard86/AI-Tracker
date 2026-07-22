namespace Hap.Domain.Assessments;

/// <summary>
/// One person's assessment for one cycle (data-model.md "Assessment"; **one per person per cycle**).
/// Relocated here from <c>Hap.Api.Authorization</c> by HAP-8 (HAP-5 handoff): the type must live in
/// <c>Hap.Domain</c> so <c>HapDbContext</c> (in <c>Hap.Infrastructure</c>) can register it as a mapped
/// entity without a layer inversion. Access to the mapped table remains seam-only — the type being in
/// the domain is a *definition* location, not a query path; no <c>DbSet</c> is exposed and the only
/// code that queries the table is the seam's assessment store (architecture-guarded, research D1).
///
/// <para>Forward-only state machine (<see cref="AssessmentState"/>). HAP-8 exercises
/// <see cref="Start"/>/<see cref="Submit"/>; HAP-9's <see cref="Moderate"/> writes
/// <see cref="ModeratedAt"/>/<see cref="ModeratedByPersonId"/> on the <c>Submitted → Moderated</c>
/// transition. <see cref="Unmoderated"/> remains HAP-10's (set by cycle-close auto-adoption when no
/// manager review completed, FR-068) and is never written here.</para>
/// </summary>
public sealed class Assessment
{
    public Guid Id { get; private set; }
    public Guid CycleId { get; private set; }
    public Guid PersonId { get; private set; }
    public AssessmentState State { get; private set; }
    public DateTime? SubmittedAt { get; private set; }
    public DateTime? ModeratedAt { get; private set; }
    public Guid? ModeratedByPersonId { get; private set; }

    /// <summary>Set true by cycle-close auto-adoption when no manager review completed (FR-068) — HAP-10.
    /// Always false through the self-assessment path.</summary>
    public bool Unmoderated { get; private set; }

    public DateTime CreatedAt { get; private set; }

    // EF materialisation constructor. Private so the only way to make a NEW assessment in code is
    // through the Start factory (which fixes the initial state), never with an arbitrary state.
    private Assessment()
    {
    }

    /// <summary>Opens a fresh assessment for (person, cycle) in <see cref="AssessmentState.InProgress"/>.
    /// Called on the first self-score write of a cycle (there is no persisted row until the individual
    /// actually enters something — viewing the empty form creates nothing).</summary>
    public static Assessment Start(Guid cycleId, Guid personId) =>
        new()
        {
            Id = Guid.NewGuid(),
            CycleId = cycleId,
            PersonId = personId,
            State = AssessmentState.InProgress,
            CreatedAt = DateTime.UtcNow,
        };

    /// <summary>InProgress → Submitted (FR-007). Idempotency and completeness are the caller's gate;
    /// this method enforces only the forward-only transition. Throws if the assessment is not currently
    /// InProgress (a NotStarted assessment has no persisted row to submit; a Submitted one is a
    /// double-submit).</summary>
    public void Submit()
    {
        if (State != AssessmentState.InProgress)
        {
            throw new AssessmentStateException(Id, State, AssessmentState.Submitted);
        }

        State = AssessmentState.Submitted;
        SubmittedAt = DateTime.UtcNow;
    }

    /// <summary>Submitted → Moderated (FR-008/010): the manager review completed, and the moderated
    /// (manager) scores are now the score of record. Records who moderated (<paramref name="moderatedByPersonId"/>
    /// — the reviewer of record, which may differ from the original line manager after an FR-070
    /// escalation) and the moderation instant. Forward-only: throws if the assessment is not currently
    /// Submitted (a NotStarted/InProgress assessment has nothing to moderate; a Moderated/AutoAdopted one
    /// cannot be moderated again). The seam maps the throw to a 409.</summary>
    public void Moderate(Guid moderatedByPersonId)
    {
        if (State != AssessmentState.Submitted)
        {
            throw new AssessmentStateException(Id, State, AssessmentState.Moderated);
        }

        State = AssessmentState.Moderated;
        ModeratedAt = DateTime.UtcNow;
        ModeratedByPersonId = moderatedByPersonId;
    }
}

/// <summary>An assessment state transition was attempted from a state that does not permit it
/// (forward-only machine). The seam maps this to a 409 at the API.</summary>
public sealed class AssessmentStateException : Exception
{
    public Guid AssessmentId { get; }
    public AssessmentState From { get; }
    public AssessmentState To { get; }

    public AssessmentStateException(Guid assessmentId, AssessmentState from, AssessmentState to)
        : base($"Assessment {assessmentId} cannot transition {from} → {to}.")
    {
        AssessmentId = assessmentId;
        From = from;
        To = to;
    }
}
