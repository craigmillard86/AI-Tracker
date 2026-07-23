using Hap.Domain.Org;
using Microsoft.EntityFrameworkCore;

namespace Hap.Infrastructure.Notifications;

/// <summary>
/// Produces a CANDIDATE notification recipient ("BU lead") per business unit from an explicit, BU-anchored
/// <see cref="OrgRole.BuDelegate"/> role grant — the structural anchor the visibility seam uses to promote
/// a reader to a BU role (<c>Hap.Api.Authorization.AssessmentReads.ClassifyReader</c> promotes a
/// <c>BuDelegate</c> grant to <c>SeamRole.BuLead</c>, and <c>RoleScope.IndividualReadCapability</c> then
/// grants <c>IndividualReadReach.BusinessUnit</c>).
///
/// <para><b>The BuDelegate anchor is NECESSARY but NOT SUFFICIENT — this resolver is a candidate producer,
/// not the final authority (HAP-18 L3 re-review 2026-07-23).</b> The seam's real read predicate
/// (<c>AssessmentReads.AuthorizeIndividualRead</c>) has three conjuncts, and this resolver replicates only
/// the anchor. It deliberately does NOT re-encode the other two, because they cannot be evaluated soundly
/// here: (1) grant PRECEDENCE — a candidate co-holding a HigExecutive / PlatformAdmin / GroupViewer grant is
/// classified as that higher role and STRIPPED of individual-read capability, so the seam would DENY them
/// despite the BuDelegate grant; and (2) reader ELIGIBILITY — active + non-contractor. Those conjuncts are
/// applied by the CONSUMERS: the FR-061 assessment-participation summary re-classifies the candidate through
/// the seam itself in <c>CycleReminderJob.BuLeadPassesSeamReadGate</c> (Hap.Api, which can see the seam);
/// the FR-037 register escalation applies the eligibility conjunct over plain <c>Person</c> attributes in
/// <c>NotificationJobService.IsEligibleRecipient</c> (Hap.Infrastructure must not depend on
/// Hap.Api.Authorization). So a candidate this resolver surfaces is NOT, on its own, guaranteed to be
/// seam-permitted — the consumer's gate closes that gap.</para>
///
/// <para><b>Why NOT <c>HierarchyRoleResolver</c>'s depth-from-root label (the previous implementation,
/// <c>HierarchyRoleResolverBuLeadAdapter</c>).</b> That label is a depth heuristic explicitly caveated
/// (Q-014) as unsafe for visibility scope: on a non-uniform tree it can name a structurally-unrelated
/// person as a BU's "lead" (HAP-18 QA Findings 1 &amp; 2). The seam therefore never trusts it — and
/// neither must the recipient selection for the FR-061 non-responder participation summary or the FR-037
/// overdue-initiative escalation, both of which disclose a BU's data to whoever they address. The prior
/// resolver leaked exactly that: a depth-mislabeled person received a BU's summary despite being denied a
/// direct <c>AuthorizeIndividualRead</c> of the very subject the summary counted.</para>
///
/// <para>Grants are re-read from the database (HAP-4 A3: a BU-scoped grant's anchoring BU lives only in
/// the row, never in a cookie claim). At most one recipient per BU (honouring the <see cref="IBuLeadResolver"/>
/// contract) — deterministic (earliest grant, then person id) when a BU has several BU delegates. A BU with
/// no <c>BuDelegate</c> grant is simply absent from the map, so the caller applies its existing
/// "skip silently but count" vacant-lead fallback rather than sending to a depth-label-mislabeled person.</para>
/// </summary>
public sealed class RoleGrantBuLeadResolver : IBuLeadResolver
{
    private readonly HapDbContext _db;

    public RoleGrantBuLeadResolver(HapDbContext db) => _db = db;

    public async Task<IReadOnlyDictionary<Guid, Guid>> ResolveBuLeadsByBusinessUnitAsync(
        CancellationToken cancellationToken = default)
    {
        // Only BU-anchored delegate grants entitle a reader to a BU's individual data (the seam's own
        // rule). Re-read from the row — never a cookie claim (HAP-4 A3).
        var grants = await _db.RoleGrants
            .Where(g => g.Role == OrgRole.BuDelegate && g.BusinessUnitId != null)
            .Select(g => new { BusinessUnitId = g.BusinessUnitId!.Value, g.PersonId, g.CreatedAt })
            .ToListAsync(cancellationToken);

        return grants
            .GroupBy(g => g.BusinessUnitId)
            .ToDictionary(
                group => group.Key,
                // One recipient per BU, deterministically: the earliest-granted delegate (ties broken by
                // person id) — so multiple delegates on one BU never make the run non-deterministic.
                // COUPLING NOTE (HAP-18 L3 re-review 2026-07-23): "earliest grant wins" is only correct while
                // grants are append-only with no revocation. When the revocation-by-superseding-record story
                // lands, this ordering must switch to "latest ACTIVE grant" (a superseded/revoked grant must
                // not win) — revisit here and in CycleReminderJob.BuLeadPassesSeamReadGate /
                // NotificationJobService.IsEligibleRecipient together.
                group => group.OrderBy(x => x.CreatedAt).ThenBy(x => x.PersonId).First().PersonId);
    }
}
