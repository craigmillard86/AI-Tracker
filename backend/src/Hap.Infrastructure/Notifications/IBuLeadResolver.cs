namespace Hap.Infrastructure.Notifications;

/// <summary>
/// Port for resolving, in one batched call, which person (if any) is the BU-lead notification recipient
/// for each business unit. The recipient MUST be someone the visibility seam would permit to read that
/// BU's individual data (both consumers — FR-061 non-responder summaries and FR-037 overdue-initiative
/// escalations — disclose a BU's data to whoever they address), so the concrete resolver
/// (<see cref="RoleGrantBuLeadResolver"/>) anchors on an explicit BU-scoped <c>OrgRole.BuDelegate</c>
/// grant, exactly as <c>Hap.Api.Authorization.AssessmentReads.ClassifyReader</c> does.
///
/// <para>It is deliberately NOT resolved from <c>Hap.Api.Identity.HierarchyRoleResolver</c>'s
/// depth-from-root label: that label is unsafe for visibility scope (Q-014) and mislabels a
/// structurally-unrelated person as a BU's lead on a non-uniform tree (HAP-18 QA Findings 1 &amp; 2).
/// The port lives in Hap.Infrastructure (not Hap.Api) because the concrete resolver needs only
/// <c>HapDbContext</c>; keeping it a port preserves the "identity is a port" convention.</para>
/// </summary>
public interface IBuLeadResolver
{
    /// <summary>At most one entry per business unit id. A BU with no BU-anchored <c>BuDelegate</c> grant —
    /// i.e. no seam-entitled BU lead — is simply absent from the result, never a thrown error; the caller
    /// applies its "skip silently but count" vacant-lead fallback.</summary>
    Task<IReadOnlyDictionary<Guid, Guid>> ResolveBuLeadsByBusinessUnitAsync(CancellationToken cancellationToken = default);
}
