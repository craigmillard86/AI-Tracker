namespace Hap.Domain.Cycles;

/// <summary>
/// One global monthly assessment cycle for a framework version (spec Key Entities; FR-002/FR-060).
/// Forward-only state machine: Draft → Open → Closed — no other transition is ever permitted, at
/// any state (<see cref="Open"/>/<see cref="Close"/> both reject a call from the wrong state).
///
/// <para><b>"One Open cycle per framework" (FR-002) is NOT enforced here</b> — it depends on every
/// other <see cref="Cycle"/> row for the same framework, which only a database-backed check can see.
/// That invariant is <c>Hap.Infrastructure.Cycles.CycleService.OpenAsync</c>'s job, checked before
/// this method is called. Likewise, invitation generation at open (FR-003) needs the whole
/// active-person population and lives in that same infrastructure service — this entity only holds
/// the state machine and the pure <see cref="AllowsSubmission"/> rule.</para>
///
/// <para><b><see cref="ContractorExclusionEnabled"/></b> is fixed at creation (FR-005: configurable
/// per cycle) and only ever read, never mutated after <see cref="Create"/>.</para>
/// </summary>
public sealed class Cycle
{
    public Guid Id { get; private set; }
    public Guid FrameworkVersionId { get; private set; }
    public string Name { get; private set; }
    public CycleState State { get; private set; }
    public bool ContractorExclusionEnabled { get; private set; }
    public DateTime? OpensAt { get; private set; }
    public DateTime? ClosesAt { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public Cycle(
        Guid id,
        Guid frameworkVersionId,
        string name,
        CycleState state,
        bool contractorExclusionEnabled,
        DateTime? opensAt,
        DateTime? closesAt,
        DateTime createdAt)
    {
        Id = id;
        FrameworkVersionId = frameworkVersionId;
        Name = name;
        State = state;
        ContractorExclusionEnabled = contractorExclusionEnabled;
        OpensAt = opensAt;
        ClosesAt = closesAt;
        CreatedAt = createdAt;
    }

    public static Cycle Create(Guid frameworkVersionId, string name, bool contractorExclusionEnabled) =>
        new(Guid.NewGuid(), frameworkVersionId, name, CycleState.Draft, contractorExclusionEnabled,
            opensAt: null, closesAt: null, DateTime.UtcNow);

    /// <summary>Draft → Open only. Sets <see cref="OpensAt"/> to now. The cross-row "one Open per
    /// framework" invariant is the caller's job (see class doc).</summary>
    public void Open()
    {
        if (State != CycleState.Draft)
        {
            throw new CycleStateTransitionException(Id, State, CycleState.Open);
        }

        State = CycleState.Open;
        OpensAt = DateTime.UtcNow;
    }

    /// <summary>Open → Closed only. Sets <see cref="ClosesAt"/> to now. Forward-only: a Draft cycle
    /// cannot close, and a Closed cycle cannot close again.</summary>
    public void Close()
    {
        if (State != CycleState.Open)
        {
            throw new CycleStateTransitionException(Id, State, CycleState.Closed);
        }

        State = CycleState.Closed;
        ClosesAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Whether a submission against this cycle is currently accepted: freely while Open; only with
    /// a late override once Closed; never while still Draft. <paramref name="hasLateOverride"/> is
    /// the caller's answer to "does a <see cref="CycleLateOverride"/> row exist for this person on
    /// this cycle" — this method stays pure and database-free by design. HAP-8/HAP-9 own the actual
    /// submission write path and must consult this rule there (QUESTIONS.md Q-017a); no such write
    /// path exists yet for this method to gate directly.
    /// </summary>
    public bool AllowsSubmission(bool hasLateOverride) =>
        State switch
        {
            CycleState.Open => true,
            CycleState.Closed => hasLateOverride,
            _ => false,
        };
}
