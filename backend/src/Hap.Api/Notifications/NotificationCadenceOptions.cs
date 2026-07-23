namespace Hap.Api.Notifications;

/// <summary>
/// The FR-061 cycle-reminder cadence, held as configuration rather than a database column
/// (QUESTIONS.md Q-031, owner-provisional): an Open cycle has no stored planned close date — the
/// domain <c>Cycle</c> stamps <c>ClosesAt</c> only at the moment it is actually closed — so "days
/// before close" is computed from a CONFIGURED intended-close reference: <c>OpensAt</c> plus
/// <see cref="CycleLengthDays"/>. This keeps HAP-18 migration-free (no schema change). The thresholds
/// themselves are configuration too, defaulting to the FR-061 amendment (2026-07-21): non-responder
/// reminders at 7/3/1 days before close, escalation summaries from 3 days before close.
///
/// <para>Calibration only — a misconfigured length shifts notification TIMING, never a privacy or
/// reconciliation guarantee (Q-031: "calibration risk, not a correctness/privacy issue"). If the owner
/// later prefers an explicit <c>ScheduledCloseAt</c> column, that is a future migration story; this
/// config seam drops out cleanly when it lands.</para>
/// </summary>
public sealed class NotificationCadenceOptions
{
    /// <summary>Configuration section name bound in Program.cs.</summary>
    public const string SectionName = "NotificationCadence";

    /// <summary>The configured cycle length in days, added to a cycle's <c>OpensAt</c> to derive its
    /// intended close date (the reference the "days before close" thresholds are measured against).
    /// Defaults to a nominal monthly length (the platform runs a monthly cycle, root spec §3).</summary>
    public int CycleLengthDays { get; set; } = 30;

    /// <summary>Days-before-close on which a non-responder receives a reminder. FR-061 amendment
    /// default: 7, 3, and 1 days before close. A reminder fires when the whole-day count until the
    /// intended close equals one of these.</summary>
    public int[] NonResponderReminderDaysBeforeClose { get; set; } = { 7, 3, 1 };

    /// <summary>Days-before-close on which manager/BU-lead escalation summaries fire. FR-061 amendment
    /// default: from 3 days before close (a single 3-day threshold; add more entries to escalate on
    /// additional days).</summary>
    public int[] EscalationDaysBeforeClose { get; set; } = { 3 };
}
