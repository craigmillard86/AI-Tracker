namespace Hap.Domain.Frameworks;

/// <summary>Lifecycle of a <see cref="FrameworkVersion"/> (data-model.md "Framework").
/// <see cref="Draft"/> versions are being authored; exactly the current in-use version per
/// framework is <see cref="Active"/> (drives <c>GET /api/frameworks/current</c>);
/// <see cref="Retired"/> versions remain for historic assessments but no longer serve new
/// cycles. None of this bears on <see cref="FrameworkVersion.IsLocked"/> (FR-054), which is
/// the separate, one-way immutability flip a Cycle referencing the version triggers.</summary>
public enum FrameworkVersionStatus
{
    Draft,
    Active,
    Retired,
}
