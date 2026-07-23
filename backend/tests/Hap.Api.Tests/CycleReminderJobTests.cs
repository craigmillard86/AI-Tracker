using System.Net;
using System.Net.Http.Json;
using Hap.Api.Identity;
using Hap.Api.Notifications;
using Hap.Infrastructure;
using Hap.Infrastructure.Directory;
using Hap.Infrastructure.Frameworks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hap.Api.Tests;

/// <summary>
/// HAP-18 FR-061 cycle reminders + escalations (root spec §4.2; Q-031 configured intended-close). Proves,
/// against a pinned clock (so days-before-close is exact, not wall-clock-flaky): non-responders are
/// reminded and submitters are not; reminders fire only on the configured threshold days (default 7/3/1);
/// at the escalation threshold (default 3) each reviewer of record gets their team's incomplete list BY
/// NAME while a capability-stripped reviewer (Platform Admin) gets none; a BU lead gets a per-team COUNT
/// summary naming no individual; a BU with no resolvable lead is skipped-but-counted; same-day re-runs
/// are idempotent; and no Open cycle is a clean no-op.
///
/// <para>Fixture (BU_A, depth-from-root so the tiers resolve): EXEC (root) → PFLEAD → GRPLEAD → BULEAD_A
/// (BU_A's BU lead) → MGR_A → {EMP_A1, EMP_A2}. EMPADM reports directly to ADMIN (a Platform Admin, whose
/// explicit grant strips individual-read capability), so ADMIN is EMPADM's reviewer of record but must
/// receive NO escalation. BU_B holds a lone OWNER_B with no leadership chain → no resolvable BU lead.</para>
/// </summary>
[Collection("hap-db")]
public sealed class CycleReminderJobTests
{
    private readonly HapApiFactory _factory;

    public CycleReminderJobTests(HapApiFactory factory) => _factory = factory;

    private const string ReminderSubjectMarker = "Reminder: submit your";
    private const string ManagerEscalationSubjectMarker = "Team members with an outstanding";
    private const string BuLeadSummarySubjectMarker = "by team";

    private RecordingEmailSender Recorder => (RecordingEmailSender)_factory.Emails.Inner;

    private async Task<HttpClient> SeedAsync()
    {
        await _factory.ResetAsync();
        _factory.Emails.Inner = new RecordingEmailSender();

        var bus = new[]
        {
            Snap.Bu("BU_A", group: "Group A", portfolio: "Portfolio 1"),
            Snap.Bu("BU_B", group: "Group A", portfolio: "Portfolio 1"),
        };
        var people = new[]
        {
            Snap.Person("ADMIN", "BU_A"),
            Snap.Person("EXEC", "BU_A"),                                    // root: no manager
            Snap.Person("PFLEAD", "BU_A", managerExternalRef: "EXEC"),
            Snap.Person("GRPLEAD", "BU_A", managerExternalRef: "PFLEAD"),
            Snap.Person("BULEAD_A", "BU_A", managerExternalRef: "GRPLEAD"), // depth 3 + has reports → BU lead
            Snap.Person("MGR_A", "BU_A", managerExternalRef: "BULEAD_A"),
            Snap.Person("EMP_A1", "BU_A", managerExternalRef: "MGR_A", name: "Alice Submitter"),
            Snap.Person("EMP_A2", "BU_A", managerExternalRef: "MGR_A", name: "Bob Pending"),
            // Reviewer of record = ADMIN (Platform Admin, capability-stripped): must get NO escalation.
            Snap.Person("EMPADM", "BU_A", managerExternalRef: "ADMIN", name: "Zed Solo"),
            // Lone person in BU_B — no leadership chain, so BU_B has no resolvable BU lead (skip case).
            Snap.Person("OWNER_B", "BU_B"),
        };

        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(bus, people));
        using (var scope = _factory.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            foreach (var bu in await db.BusinessUnits.ToListAsync())
            {
                bu.SetOnboarded(true);
            }
            await db.SaveChangesAsync();
        }

        var seed = new List<SeedUserRecord> { Snap.SeedUser("ADMIN", role: "Platform Admin", buCode: "BU_A") };
        seed.AddRange(people
            .Where(p => p.ExternalRef != "ADMIN")
            .Select(p => Snap.SeedUser(p.ExternalRef, role: "Individual", buCode: p.BuCode)));
        _factory.SeedUsers.Inner = new StubSeedUserSource(seed);

        var admin = _factory.CreateClient();
        await HapApiFactory.SignInAsync(admin, "ADMIN");

        // Seed the framework (dimensions) then create + open a cycle so invitations exist (FR-003).
        var framework = await (await admin.PostAsync("/api/admin/frameworks", null))
            .Content.ReadFromJsonAsync<FrameworkSeedResult>();
        var created = await admin.PostAsJsonAsync("/api/cycles",
            new CreateCycleRequest(framework!.VersionId, "2026-08", true));
        var cycle = await created.Content.ReadFromJsonAsync<CycleResponse>();
        await admin.PostAsync($"/api/cycles/{cycle!.Id}/open", null);

        return admin;
    }

    /// <summary>The Open cycle's OpensAt and the configured cycle length, so a test can pin
    /// <c>asOf = OpensAt + (length - T)</c> to land exactly T whole days before the intended close.</summary>
    private async Task<(DateTime OpensAt, int Length)> CycleClockAsync()
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var opensAt = (await db.Cycles.OrderByDescending(c => c.OpensAt).FirstAsync()).OpensAt!.Value;
        var length = scope.ServiceProvider.GetRequiredService<IOptions<NotificationCadenceOptions>>().Value.CycleLengthDays;
        return (opensAt, length);
    }

    private async Task<DateTime> AsOfDaysBeforeCloseAsync(int daysBeforeClose)
    {
        var (opensAt, length) = await CycleClockAsync();
        return opensAt.AddDays(length - daysBeforeClose);
    }

    private async Task<CycleReminderRunResult> RunJobAsync(DateTime asOf)
    {
        using var scope = _factory.NewScope();
        var job = scope.ServiceProvider.GetRequiredService<CycleReminderJob>();
        return await job.RunAsync(asOf);
    }

    private async Task SubmitSelfAsync(string externalRef, int score)
    {
        var client = _factory.CreateClient();
        await HapApiFactory.SignInAsync(client, externalRef);
        var form = (await (await client.GetAsync("/api/me/assessment")).Content.ReadFromJsonAsync<SelfAssessmentResponse>())!;
        await client.PutAsJsonAsync("/api/me/assessment/scores",
            new UpsertScoresRequest(form.Dimensions.Select(d => new ScoreEntry(d.DimensionId, score, null)).ToList()));
        var submit = await client.PostAsync("/api/me/assessment/submit", null);
        Assert.Equal(HttpStatusCode.NoContent, submit.StatusCode);
    }

    private async Task<string> PersonEmailAsync(string externalRef)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return (await db.People.SingleAsync(p => p.ExternalRef == externalRef)).Email;
    }

    /// <summary>Grants <paramref name="externalRef"/> an explicit BU-scoped <c>BuDelegate</c> role over
    /// <paramref name="buCode"/> — the seam's own BU-lead anchor. The BU-lead summary recipient is chosen
    /// from this grant (not a hierarchy depth label), so a positive "the BU lead gets the summary" case
    /// must make the recipient a genuinely seam-entitled BU delegate (HAP-18 QA Finding 1 fix).</summary>
    private async Task GrantBuDelegateAsync(string externalRef, string buCode)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var personId = (await db.People.SingleAsync(p => p.ExternalRef == externalRef)).Id;
        var buId = (await db.BusinessUnits.SingleAsync(b => b.Code == buCode)).Id;
        db.RoleGrants.Add(Hap.Domain.Org.RoleGrant.Create(personId, Hap.Domain.Org.OrgRole.BuDelegate, buId, grantedBy: "test"));
        await db.SaveChangesAsync();
    }

    // ==================================================================================================
    // Non-responder reminders (default 7/3/1 days before close)
    // ==================================================================================================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task At_seven_days_reminders_go_to_non_responders_and_never_to_a_submitter()
    {
        await SeedAsync();
        await SubmitSelfAsync("EMP_A1", 2); // EMP_A1 has responded; everyone else has not

        var result = await RunJobAsync(await AsOfDaysBeforeCloseAsync(7));

        // T-7 is a reminder day, not an escalation day (default escalation = {3}).
        Assert.Equal(0, result.ManagerEscalationsSent);
        Assert.Equal(0, result.BuLeadSummariesSent);
        Assert.True(result.NonResponderRemindersSent > 0);

        // Every send this run is a reminder (no escalations at T-7), and every send is a reminder subject.
        Assert.Equal(result.NonResponderRemindersSent, Recorder.Sent.Count);
        Assert.All(Recorder.Sent, m => Assert.Contains(ReminderSubjectMarker, m.Subject));

        var submitterEmail = await PersonEmailAsync("EMP_A1");
        var pendingEmail = await PersonEmailAsync("EMP_A2");
        Assert.DoesNotContain(Recorder.Sent, m => m.To.Contains(submitterEmail)); // submitter reminded of nothing
        Assert.Contains(Recorder.Sent, m => m.To.Contains(pendingEmail));         // non-responder reminded
    }

    [Fact]
    public async Task Off_threshold_days_send_nothing()
    {
        await SeedAsync();

        // 10 days before close is not in the default reminder {7,3,1} or escalation {3} sets.
        var result = await RunJobAsync(await AsOfDaysBeforeCloseAsync(10));

        Assert.Equal(new CycleReminderRunResult(0, 0, 0, 0), result);
        Assert.Empty(Recorder.Sent);
    }

    // ==================================================================================================
    // Escalations (default from 3 days before close)
    // ==================================================================================================

    [Fact]
    public async Task At_three_days_a_manager_escalation_lists_the_teams_incomplete_members_only()
    {
        await SeedAsync();
        await SubmitSelfAsync("EMP_A1", 2); // EMP_A1 done; EMP_A2 outstanding under MGR_A

        var result = await RunJobAsync(await AsOfDaysBeforeCloseAsync(3));

        Assert.True(result.ManagerEscalationsSent > 0);

        var mgrEmail = await PersonEmailAsync("MGR_A");
        var escalation = Assert.Single(Recorder.Sent,
            m => m.To.Contains(mgrEmail) && m.Subject.Contains(ManagerEscalationSubjectMarker));
        Assert.Contains("Bob Pending", escalation.Body);         // EMP_A2 is outstanding
        Assert.DoesNotContain("Alice Submitter", escalation.Body); // EMP_A1 submitted → not listed
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task At_three_days_a_capability_stripped_reviewer_of_record_gets_no_manager_escalation()
    {
        await SeedAsync(); // no submissions — EMPADM is outstanding, its reviewer of record is ADMIN

        await RunJobAsync(await AsOfDaysBeforeCloseAsync(3));

        var adminEmail = await PersonEmailAsync("ADMIN");
        // ADMIN (Platform Admin) may still get a self-reminder as an invited participant, but NEVER a
        // manager escalation — a capability-stripped role receives no report's participation status.
        Assert.DoesNotContain(Recorder.Sent,
            m => m.To.Contains(adminEmail) && m.Subject.Contains(ManagerEscalationSubjectMarker));
        // and EMPADM's name never appears in ANY manager escalation (its only reviewer was ADMIN, skipped).
        Assert.DoesNotContain(Recorder.Sent,
            m => m.Subject.Contains(ManagerEscalationSubjectMarker) && m.Body.Contains("Zed Solo"));
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task At_three_days_the_BU_lead_gets_a_per_team_count_summary_naming_no_individual()
    {
        await SeedAsync();
        // BULEAD_A is BU_A's seam-entitled lead via an explicit BU-anchored BuDelegate grant — the same
        // anchor AssessmentReads uses. (A depth-from-root "BU lead" is deliberately NOT a valid recipient
        // any more — HAP-18 QA Finding 1.) BU_B has no such grant → skipped but counted.
        await GrantBuDelegateAsync("BULEAD_A", "BU_A");
        await SubmitSelfAsync("EMP_A1", 2);

        var result = await RunJobAsync(await AsOfDaysBeforeCloseAsync(3));

        // BU_A has a resolvable lead (BULEAD_A); BU_B has none → skipped but counted.
        Assert.Equal(1, result.BuLeadSummariesSent);
        Assert.Equal(1, result.BuLeadSummariesSkippedNoLead);

        var buLeadEmail = await PersonEmailAsync("BULEAD_A");
        var summary = Assert.Single(Recorder.Sent,
            m => m.To.Contains(buLeadEmail) && m.Subject.Contains(BuLeadSummarySubjectMarker));
        Assert.Contains("Team led by", summary.Body);        // per-team breakdown
        Assert.Contains("not yet submitted", summary.Body);  // with counts
        // No individual non-responder is named in the BU-lead summary (counts only).
        Assert.DoesNotContain("Bob Pending", summary.Body);
        Assert.DoesNotContain("Zed Solo", summary.Body);
    }

    // ==================================================================================================
    // Idempotence + no-op
    // ==================================================================================================

    [Fact]
    public async Task Running_twice_the_same_day_sends_no_duplicate_reminders()
    {
        await SeedAsync();

        var asOf = await AsOfDaysBeforeCloseAsync(7);
        var first = await RunJobAsync(asOf);
        var countAfterFirst = Recorder.Sent.Count;
        var second = await RunJobAsync(asOf.AddHours(4)); // later the same UTC day

        Assert.True(first.NonResponderRemindersSent > 0);
        Assert.Equal(0, second.NonResponderRemindersSent);
        Assert.Equal(countAfterFirst, Recorder.Sent.Count); // no new mail on the second run
    }

    [Fact]
    public async Task With_no_open_cycle_the_job_is_a_no_op()
    {
        await _factory.ResetAsync();
        _factory.Emails.Inner = new RecordingEmailSender();

        var result = await RunJobAsync(DateTime.UtcNow);

        Assert.Equal(new CycleReminderRunResult(0, 0, 0, 0), result);
        Assert.Empty(Recorder.Sent);
    }

    // ==================================================================================================
    // Wire contract — POST /api/admin/notifications/run reports the cycle-reminder counts
    // ==================================================================================================

    [Fact]
    public async Task Endpoint_reports_the_cycle_reminder_count_keys()
    {
        var admin = await SeedAsync();

        var response = await admin.PostAsync("/api/admin/notifications/run", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var counts = await response.Content.ReadFromJsonAsync<Dictionary<string, int>>();
        Assert.NotNull(counts);
        // The dictionary reports a count per cycle-reminder notification type (values are 0 here because the
        // endpoint runs at real UtcNow, ~a full cycle length before the intended close — the pinned-clock
        // tests above prove the sends themselves).
        Assert.True(counts!.ContainsKey("CycleReminders"));
        Assert.True(counts.ContainsKey("CycleManagerEscalations"));
        Assert.True(counts.ContainsKey("CycleBuLeadSummaries"));
        Assert.True(counts.ContainsKey("CycleBuLeadSummariesSkippedNoLead"));
    }
}
