namespace Hap.Api.Authorization;

/// <summary>How the seam treats a contractor who is a line manager (QUESTIONS.md Q-006). Default
/// <see cref="Restrictive"/>: a contractor manager gets no individual-score access and their reports'
/// reviews escalate to the first non-contractor ancestor. Uncertainty rounds up on the safeguarding
/// seam (constitution Art. V) until Q-006 is owner/DPIA-ratified — flipping to <see cref="Permissive"/>
/// is an L3 change to this default plus a seam-test update, not a config tweak.</summary>
public enum ContractorManagerPolicy
{
    Restrictive,
    Permissive,
}

/// <summary>
/// Configuration knobs for the visibility seam, injected as a singleton. Every default here is the
/// SAFE posture; a non-default value is a reviewed L3 decision, never an ambient config flip.
/// </summary>
public sealed record SeamOptions
{
    /// <summary>Restrictive by default (Q-006): contractor line managers are excluded from
    /// individual-score access.</summary>
    public ContractorManagerPolicy ContractorManagerPolicy { get; init; } = ContractorManagerPolicy.Restrictive;

    /// <summary>Hard cap on a management-chain walk length — a backstop bounding work against a cyclic
    /// or pathological chain the resolver did not itself validate. The visited-set already guarantees
    /// termination; this documents the "no unbounded org depth" assumption and stops a runaway walk on
    /// hostile data. Real HIG chain depth is ~6, so 64 is comfortably clear of legitimate depth.</summary>
    public int MaxChainDepth { get; init; } = 64;

    /// <summary>The safe default instance.</summary>
    public static readonly SeamOptions Default = new();
}
