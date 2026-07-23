namespace Hap.Api.Authorization;

/// <summary>
/// Registers the visibility seam (CLAUDE.md §2: <c>Hap.Api/Authorization</c> is THE visibility seam).
/// Mirrors the identity/infrastructure wiring extensions, keeping composition out of Program.cs.
///
/// <para>Registered now: the seam foundation (org side + <see cref="OrgGraphLoader"/>) AND — from
/// HAP-8 — the assessment-read gateway (<see cref="AssessmentReads"/>) over the real DbSet-backed
/// store (<see cref="SeamAssessmentStore"/>), plus the self-scope workflow
/// (<see cref="SelfAssessmentService"/>). These are scoped: they wrap the request-scoped
/// <c>HapDbContext</c>. The store is the ONLY registered <see cref="IAssessmentStore"/>, so every
/// production assessment query funnels through the seam.</para>
/// </summary>
public static class AuthorizationServiceCollectionExtensions
{
    public static IServiceCollection AddHapAuthorization(this IServiceCollection services)
    {
        services.AddSingleton(SeamOptions.Default);
        services.AddSingleton<ChainResolver>();
        services.AddSingleton<SuppressionEvaluator>();
        // The shared rollup pipeline (HAP-11): the single "moderated scores → per-node rollups + frozen
        // suppression" definition, used by BOTH the cycle-close snapshot writer and the live open-cycle
        // dashboard reader, so the two agree by construction (research D4). Its DB reads take the context as
        // a parameter (it holds only the suppression evaluator + a logger), so singleton is safe.
        services.AddSingleton<RollupPipeline>();
        services.AddScoped<OrgGraphLoader>();

        // The assessment seam (HAP-8): one DbSet-backed store behind both storage ports (cross-person
        // read + self-scope), the read gateway, and the self-scope workflow. Scoped — each wraps the
        // request-scoped HapDbContext; the two port registrations forward to the single store instance.
        services.AddScoped<SeamAssessmentStore>();
        services.AddScoped<IAssessmentStore>(sp => sp.GetRequiredService<SeamAssessmentStore>());
        services.AddScoped<ISelfAssessmentStore>(sp => sp.GetRequiredService<SeamAssessmentStore>());
        services.AddScoped<AssessmentReads>();
        services.AddScoped<SelfAssessmentService>();
        // The manager-scope moderation workflow (HAP-9): reviews queue, the [A] member read (fail-closed
        // IndividualView audit), and the moderation write (submission-lock + Δ≥2 comment + ScoreChange
        // audit). Wraps the same request-scoped store/gateway/context — a cross-person read never escapes
        // the seam.
        services.AddScoped<ManagerModerationService>();

        // The aggregate-read gateway (HAP-11): BU dashboard, org rollups, own-team summary. Scope decided
        // in-seam (hierarchy anchors + explicit grants, Q-024); every figure projected through the F2
        // suppression guard so a suppressed node emits no number. Scoped — wraps the request-scoped context;
        // depends on HierarchyRoleResolver (registered by AddHapIdentity) for aggregate-scope anchors only.
        services.AddScoped<RollupReads>();

        // The cycle-close processor (HAP-10): auto-adoption + rollup snapshots + frozen suppression.
        // It implements Hap.Infrastructure's ICycleCloseProcessor port but LIVES here because it reads
        // moderated scores (seam-only, research D1); CycleService.CloseAsync resolves it and runs it in
        // the close transaction. Scoped — same request-scoped HapDbContext as the rest of the seam.
        services.AddScoped<Hap.Infrastructure.Cycles.ICycleCloseProcessor, CycleCloseProcessor>();

        // The audit & GDPR surfaces (HAP-12, L3): the right-of-access export (self-scope, fail-closed Export
        // audit row), the retention erasure job (nulls raw values > 3y, one RetentionErasure row per
        // assessment, idempotent via the audit ledger), and the read-only audit search. Export + retention
        // wrap the request-scoped seam store (assessment writes stay in-seam); the audit search reads the
        // public AuditLogs set. All scoped — same request-scoped HapDbContext as the rest of the seam.
        // The single erasure-ledger source (HAP-12) — the authoritative "which assessments are retention-
        // erased" signal every raw-score display read, the export, the moderation interlock, and the
        // retention idempotency check consult (enforced structurally by SeamBoundaryTests). Scoped.
        services.AddScoped<ErasureLedger>();
        services.AddScoped<PersonalDataExportService>();
        services.AddScoped<RetentionService>();
        services.AddScoped<AuditQueryService>();
        return services;
    }
}
