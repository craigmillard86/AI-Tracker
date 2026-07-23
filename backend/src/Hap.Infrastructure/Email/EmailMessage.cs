namespace Hap.Infrastructure.Email;

/// <summary>A plain-text outbound email (FR-037/FR-057/FR-061 — no HTML requirement anywhere in the
/// spec for this story, so plain text keeps the sender and the templates simple).</summary>
public sealed record EmailMessage(IReadOnlyList<string> To, string Subject, string Body);
