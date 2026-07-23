using System.Net;
using System.Net.Http.Json;
using Hap.Api.Identity;
using Hap.Domain.Register;
using Hap.Infrastructure;
using Hap.Infrastructure.Directory;
using Hap.Infrastructure.Email;
using Hap.Infrastructure.Notifications;
using Hap.Infrastructure.Register;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hap.Api.Tests;

/// <summary>
/// HAP-18 FR-037 "weekly update discipline" — owner nags at &gt;7d, BU Lead escalations at &gt;14d
/// (root spec §4.2), Idea/Retired exemption, idempotence, and the <c>POST /api/admin/notifications/run</c>
/// wire contract. Cycle reminders (FR-061) and moderation-complete (FR-057) are explicitly out of scope
/// for this pass — not covered here.
///
/// <para>Fixture: EXEC → PFLEAD → GRPLEAD → BULEAD_A → OWNER_A, all in BU_A (mirrors
/// <c>HierarchyRoleResolverTests</c>'s depth-from-root shape so BULEAD_A resolves as BU_A's BU Lead).
/// BU_B has a single person (OWNER_B) with no manager chain reaching a validated leadership node — BU_B
/// deliberately has NO resolvable BU Lead (the Q-014 "vacant" case).</para>
///
/// <para>Threshold/exemption assertions call <see cref="NotificationJobService"/> directly with a pinned
/// <c>asOf</c> clock (via a DI scope) so day-boundary math is exact and not wall-clock-flaky; one test
/// ("Endpoint_runs_the_job...") goes through the real HTTP endpoint instead (its <c>asOf</c> defaults to
/// real UtcNow there, so it uses a comfortably-overdue fixture rather than an exact boundary).</para>
/// </summary>
[Collection("hap-db")]
public sealed class NotificationJobEndpointsTests
{
    private readonly HapApiFactory _factory;

    public NotificationJobEndpointsTests(HapApiFactory factory) => _factory = factory;

    private static readonly DateTime FixedNow = new(2026, 7, 23, 12, 0, 0, DateTimeKind.Utc);

    private static (DirectoryBu[] Bus, DirectoryPerson[] People, SeedUserRecord[] Seed) Fixture()
    {
        var bus = new[]
        {
            Snap.Bu("BU_A", group: "Group A", portfolio: "Portfolio 1"),
            Snap.Bu("BU_B", group: "Group A", portfolio: "Portfolio 1"),
        };
        var people = new List<DirectoryPerson>
        {
            Snap.Person("ADMIN", "BU_A"),
            Snap.Person("EXEC", "BU_A"),
            Snap.Person("PFLEAD", "BU_A", managerExternalRef: "EXEC"),
            Snap.Person("GRPLEAD", "BU_A", managerExternalRef: "PFLEAD"),
            Snap.Person("BULEAD_A", "BU_A", managerExternalRef: "GRPLEAD"),
            Snap.Person("OWNER_A", "BU_A", managerExternalRef: "BULEAD_A"),
            // No manager, no reports: never resolves to a leadership label — BU_B has no BU Lead.
            Snap.Person("OWNER_B", "BU_B"),
        };
        var seed = new List<SeedUserRecord> { Snap.SeedUser("ADMIN", role: "Platform Admin") };
        seed.AddRange(people
            .Where(p => p.ExternalRef != "ADMIN")
            .Select(p => Snap.SeedUser(p.ExternalRef, role: "Individual", buCode: p.BuCode)));
        return (bus, people.ToArray(), seed.ToArray());
    }

    private async Task<HttpClient> SeedAsync()
    {
        await _factory.ResetAsync();
        _factory.Emails.Inner = new RecordingEmailSender();

        var (bus, people, seed) = Fixture();
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
            await scope.ServiceProvider.GetRequiredService<HarrisTaxonomySeeder>().SeedAsync();
        }
        _factory.SeedUsers.Inner = new StubSeedUserSource(seed.ToList());

        var admin = _factory.CreateClient();
        await HapApiFactory.SignInAsync(admin, "ADMIN");
        return admin;
    }

    private RecordingEmailSender Recorder => (RecordingEmailSender)_factory.Emails.Inner;

    private async Task<Guid> PersonIdAsync(string externalRef)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return (await db.People.SingleAsync(p => p.ExternalRef == externalRef)).Id;
    }

    private async Task<string> PersonEmailAsync(string externalRef)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return (await db.People.SingleAsync(p => p.ExternalRef == externalRef)).Email;
    }

    private async Task<Guid> BuIdAsync(string code)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return (await db.BusinessUnits.SingleAsync(b => b.Code == code)).Id;
    }

    /// <summary>Grants <paramref name="externalRef"/> an explicit BU-scoped <c>BuDelegate</c> role over
    /// <paramref name="buCode"/> — the seam's own BU-lead anchor and the one the BU-lead escalation
    /// recipient is now chosen from (not a hierarchy depth label). A positive "the BU lead gets the
    /// escalation" case must make the recipient a genuinely seam-entitled BU delegate (HAP-18 QA
    /// Finding 2 fix).</summary>
    private async Task GrantBuDelegateAsync(string externalRef, string buCode)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var personId = (await db.People.SingleAsync(p => p.ExternalRef == externalRef)).Id;
        var buId = (await db.BusinessUnits.SingleAsync(b => b.Code == buCode)).Id;
        db.RoleGrants.Add(Hap.Domain.Org.RoleGrant.Create(personId, Hap.Domain.Org.OrgRole.BuDelegate, buId, grantedBy: "test"));
        await db.SaveChangesAsync();
    }

    /// <summary>Creates an Initiative directly against the DbContext (no register-write test helper
    /// exists worth reusing beyond the domain factory itself), backdating <c>LastUpdateAt</c> via
    /// <see cref="Initiative.PostWeeklyUpdate"/> right after <see cref="Initiative.Create"/> since there
    /// is no direct setter.</summary>
    private async Task<Guid> CreateInitiativeAsync(
        string businessUnitCode, string ownerExternalRef, InitiativeStage stage, DateTime lastUpdateAt, string name)
    {
        var buId = await BuIdAsync(businessUnitCode);
        var ownerId = await PersonIdAsync(ownerExternalRef);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var categoryId = (await db.HarrisCategories.FirstAsync()).Id;

        var initiative = Initiative.Create(
            businessUnitId: buId,
            name: name,
            description: null,
            sponsorPersonId: null,
            ownerPersonId: ownerId,
            createdByPersonId: ownerId,
            categoryId: categoryId,
            aiDlcLevel: 1,
            functionsAffected: null,
            dimensionsAdvanced: null,
            customersInProduction: null,
            riskTier: RiskTier.Low);

        if (stage != InitiativeStage.Idea)
        {
            initiative.AdvanceStage(stage);
        }
        initiative.PostWeeklyUpdate(RagStatus.OnTrack, lastUpdateAt);

        db.Initiatives.Add(initiative);
        await db.SaveChangesAsync();
        return initiative.Id;
    }

    private async Task<NotificationRunResult> RunJobAsync(DateTime asOf)
    {
        using var scope = _factory.NewScope();
        var service = scope.ServiceProvider.GetRequiredService<NotificationJobService>();
        return await service.RunWeeklyUpdateNagsAsync(asOf);
    }

    // ==================================================================================================
    // Idea/Retired exemption
    // ==================================================================================================

    [Fact]
    public async Task Idea_and_Retired_stage_initiatives_are_exempt_regardless_of_how_stale()
    {
        await SeedAsync();
        await CreateInitiativeAsync("BU_A", "OWNER_A", InitiativeStage.Idea, FixedNow.AddDays(-90), "Still an Idea");
        await CreateInitiativeAsync("BU_A", "OWNER_A", InitiativeStage.Retired, FixedNow.AddDays(-90), "Retired long ago");

        var result = await RunJobAsync(FixedNow);

        Assert.Equal(0, result.OwnerNagsSent);
        Assert.Equal(0, result.BuLeadEscalationsSent);
        Assert.Empty(Recorder.Sent);
    }

    // ==================================================================================================
    // Owner nag threshold (>7d)
    // ==================================================================================================

    [Fact]
    public async Task Exactly_seven_days_since_update_is_not_yet_overdue()
    {
        await SeedAsync();
        await CreateInitiativeAsync("BU_A", "OWNER_A", InitiativeStage.Evaluation, FixedNow.AddDays(-7), "Right On The Line");

        var result = await RunJobAsync(FixedNow);

        Assert.Equal(0, result.OwnerNagsSent);
        Assert.Empty(Recorder.Sent);
    }

    [Fact]
    public async Task Eight_days_since_update_triggers_an_owner_nag_to_only_the_owner()
    {
        await SeedAsync();
        var initiativeId = await CreateInitiativeAsync(
            "BU_A", "OWNER_A", InitiativeStage.Evaluation, FixedNow.AddDays(-8), "Copilot Rollout");
        var ownerEmail = await PersonEmailAsync("OWNER_A");

        var result = await RunJobAsync(FixedNow);

        Assert.Equal(1, result.OwnerNagsSent);
        Assert.Equal(0, result.BuLeadEscalationsSent);
        var message = Assert.Single(Recorder.Sent);
        Assert.Equal(new[] { ownerEmail }, message.To);
        Assert.Contains($"WUN-{initiativeId:N}-{FixedNow:yyyyMMdd}", message.Subject);
        Assert.Contains("Copilot Rollout", message.Subject);
        Assert.Contains("8 days", message.Body);
    }

    [Theory]
    [InlineData(InitiativeStage.Evaluation)]
    [InlineData(InitiativeStage.Pilot)]
    [InlineData(InitiativeStage.Production)]
    [InlineData(InitiativeStage.Scaled)]
    public async Task Every_active_stage_is_nag_eligible(InitiativeStage stage)
    {
        await SeedAsync();
        await CreateInitiativeAsync("BU_A", "OWNER_A", stage, FixedNow.AddDays(-8), $"{stage} Initiative");

        var result = await RunJobAsync(FixedNow);

        Assert.Equal(1, result.OwnerNagsSent);
    }

    // ==================================================================================================
    // BU Lead escalation threshold (>14d) — summary of ALL >7d-overdue initiatives in the BU
    // ==================================================================================================

    [Fact]
    public async Task Exactly_fourteen_days_since_update_does_not_yet_trigger_an_escalation()
    {
        await SeedAsync();
        await CreateInitiativeAsync("BU_A", "OWNER_A", InitiativeStage.Evaluation, FixedNow.AddDays(-14), "Almost Escalating");

        var result = await RunJobAsync(FixedNow);

        Assert.Equal(1, result.OwnerNagsSent); // still an owner nag — 14 > 7
        Assert.Equal(0, result.BuLeadEscalationsSent);
        Assert.Equal(0, result.BuLeadEscalationsSkippedNoLead);
    }

    [Fact]
    public async Task Fifteen_days_triggers_one_escalation_to_the_BU_Lead_listing_every_overdue_initiative_in_the_BU()
    {
        await SeedAsync();
        // BULEAD_A is BU_A's seam-entitled lead via an explicit BU-anchored BuDelegate grant (a
        // depth-from-root "BU lead" is no longer a valid recipient — HAP-18 QA Finding 2).
        await GrantBuDelegateAsync("BULEAD_A", "BU_A");
        await CreateInitiativeAsync(
            "BU_A", "OWNER_A", InitiativeStage.Pilot, FixedNow.AddDays(-15), "Very Stale Initiative");
        await CreateInitiativeAsync(
            "BU_A", "OWNER_A", InitiativeStage.Evaluation, FixedNow.AddDays(-8), "Mildly Stale Initiative");
        var buLeadEmail = await PersonEmailAsync("BULEAD_A");

        var result = await RunJobAsync(FixedNow);

        Assert.Equal(2, result.OwnerNagsSent); // one nag per overdue initiative
        Assert.Equal(1, result.BuLeadEscalationsSent); // ONE escalation, not one per initiative
        Assert.Equal(0, result.BuLeadEscalationsSkippedNoLead);

        var escalation = Assert.Single(Recorder.Sent, m => m.To.SequenceEqual(new[] { buLeadEmail }));
        Assert.Contains($"WUE-{(await BuIdAsync("BU_A")):N}-{FixedNow:yyyyMMdd}", escalation.Subject);
        Assert.Contains("Very Stale Initiative", escalation.Body);
        Assert.Contains("15d", escalation.Body);
        Assert.Contains("Mildly Stale Initiative", escalation.Body); // lists BOTH >7d initiatives, not just the >14d one
        Assert.Contains("8d", escalation.Body);

        // exactly 3 emails total: 2 owner nags + 1 escalation, no stray recipients.
        Assert.Equal(3, Recorder.Sent.Count);
    }

    [Fact]
    public async Task A_BU_with_no_resolvable_BU_Lead_is_skipped_silently_but_counted_owner_nag_still_fires()
    {
        await SeedAsync();
        await CreateInitiativeAsync("BU_B", "OWNER_B", InitiativeStage.Production, FixedNow.AddDays(-20), "Orphaned BU Initiative");
        var ownerBEmail = await PersonEmailAsync("OWNER_B");

        var result = await RunJobAsync(FixedNow);

        Assert.Equal(1, result.OwnerNagsSent);
        Assert.Equal(0, result.BuLeadEscalationsSent);
        Assert.Equal(1, result.BuLeadEscalationsSkippedNoLead);

        var message = Assert.Single(Recorder.Sent);
        Assert.Equal(new[] { ownerBEmail }, message.To);
    }

    // ==================================================================================================
    // Idempotence — the same day's job run twice sends no duplicates
    // ==================================================================================================

    [Fact]
    public async Task Running_the_job_twice_the_same_day_sends_no_duplicate_emails()
    {
        await SeedAsync();
        await CreateInitiativeAsync("BU_A", "OWNER_A", InitiativeStage.Evaluation, FixedNow.AddDays(-8), "Once Only");

        var first = await RunJobAsync(FixedNow);
        var second = await RunJobAsync(FixedNow.AddHours(3)); // later the same UTC day

        Assert.Equal(1, first.OwnerNagsSent);
        Assert.Equal(0, second.OwnerNagsSent);
        Assert.Single(Recorder.Sent);
    }

    // ==================================================================================================
    // Wire contract — POST /api/admin/notifications/run
    // ==================================================================================================

    [Fact]
    public async Task Endpoint_runs_the_job_and_reports_counts_matching_the_dictionary_shape()
    {
        var admin = await SeedAsync();
        await CreateInitiativeAsync(
            "BU_A", "OWNER_A", InitiativeStage.Evaluation, DateTime.UtcNow.AddDays(-8), "Endpoint-Triggered Nag");

        var response = await admin.PostAsync("/api/admin/notifications/run", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var counts = await response.Content.ReadFromJsonAsync<Dictionary<string, int>>();

        Assert.NotNull(counts);
        Assert.Equal(1, counts!["WeeklyUpdateOwnerNags"]);
        Assert.Equal(0, counts["WeeklyUpdateBuLeadEscalations"]);
        Assert.Equal(0, counts["WeeklyUpdateBuLeadEscalationsSkippedNoLead"]);
        Assert.Single(Recorder.Sent);
    }

    [Fact]
    public async Task Endpoint_requires_Platform_Admin()
    {
        await SeedAsync();
        var individual = _factory.CreateClient();
        await HapApiFactory.SignInAsync(individual, "OWNER_A");

        var response = await individual.PostAsync("/api/admin/notifications/run", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ==================================================================================================
    // Config assertion — SmtpOptions has no external-host surface (AC: "no external SMTP config exists")
    // ==================================================================================================

    [Fact]
    public void SmtpOptions_defaults_point_only_at_the_local_mailpit_listener()
    {
        var options = new SmtpOptions();

        Assert.Equal("localhost", options.Host);
        Assert.Equal(1025, options.Port);
    }
}
