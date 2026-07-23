using System.Net;
using System.Net.Http.Json;
using Hap.Api.Identity;
using Hap.Api.Notifications;
using Hap.Domain.Org;
using Hap.Infrastructure;
using Hap.Infrastructure.Directory;
using Hap.Infrastructure.Frameworks;
using Hap.Infrastructure.Notifications;
using Hap.Domain.Register;
using Hap.Infrastructure.Register;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hap.Api.Tests;

/// <summary>
/// QA-window adversarial coverage for HAP-18 (fresh-instance QA pass, CLAUDE.md §9.3), added during QA
/// rather than Dev — attributed as QA work. Targets the §9.3 mandatory attempts against every
/// notification surface, beyond what <see cref="CycleReminderJobTests"/> /
/// <see cref="ModerationCompleteNotificationTests"/> already proved.
/// </summary>
[Collection("hap-db")]
public sealed class CycleReminderQaAdversarialTests
{
    private readonly HapApiFactory _factory;

    public CycleReminderQaAdversarialTests(HapApiFactory factory) => _factory = factory;

    private RecordingEmailSender Recorder => (RecordingEmailSender)_factory.Emails.Inner;

    private async Task<HttpClient> ClientAsync(string externalRef)
    {
        var client = _factory.CreateClient();
        await HapApiFactory.SignInAsync(client, externalRef);
        return client;
    }

    private async Task<string> PersonEmailAsync(string externalRef)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return (await db.People.SingleAsync(p => p.ExternalRef == externalRef)).Email;
    }

    private async Task<Guid> PersonIdAsync(string externalRef)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        return (await db.People.SingleAsync(p => p.ExternalRef == externalRef)).Id;
    }

    private async Task SubmitSelfAsync(string externalRef, int score = 2)
    {
        var client = await ClientAsync(externalRef);
        var form = (await (await client.GetAsync("/api/me/assessment")).Content.ReadFromJsonAsync<SelfAssessmentResponse>())!;
        await client.PutAsJsonAsync("/api/me/assessment/scores",
            new UpsertScoresRequest(form.Dimensions.Select(d => new ScoreEntry(d.DimensionId, score, null)).ToList()));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/me/assessment/submit", null)).StatusCode);
    }

    private async Task GrantAsync(string externalRef, OrgRole role, string? businessUnitCode = null)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var personId = (await db.People.SingleAsync(p => p.ExternalRef == externalRef)).Id;
        Guid? buId = businessUnitCode is null
            ? null
            : (await db.BusinessUnits.SingleAsync(b => b.Code == businessUnitCode)).Id;
        db.RoleGrants.Add(RoleGrant.Create(personId, role, buId, grantedBy: "qa-test"));
        await db.SaveChangesAsync();
    }

    /// <summary>Builds the seam's <see cref="Hap.Api.Authorization.CallerContext"/> for a person from
    /// their FULL set of DB role grants — mirroring the caller-context builder the seam uses at the
    /// endpoint (HAP-4 A3: a BU-scoped grant's anchoring BU is re-read from the row). Lets a premise assert
    /// what <c>AssessmentReads.AuthorizeIndividualRead</c> decides for the exact grant shape under test.</summary>
    private async Task<Hap.Api.Authorization.CallerContext> CallerContextAsync(string externalRef)
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var personId = (await db.People.SingleAsync(p => p.ExternalRef == externalRef)).Id;
        var grants = await db.RoleGrants
            .Where(g => g.PersonId == personId)
            .Select(g => new Hap.Api.Authorization.CallerGrant(g.Role, g.BusinessUnitId))
            .ToListAsync();
        return new Hap.Api.Authorization.CallerContext(personId, grants);
    }

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

    private async Task<CycleReminderRunResult> RunCycleJobAsync(DateTime asOf)
    {
        using var scope = _factory.NewScope();
        var job = scope.ServiceProvider.GetRequiredService<CycleReminderJob>();
        return await job.RunAsync(asOf);
    }

    // =====================================================================================================
    // §9.3(b) REGRESSION GUARD (was BLOCKING Finding 1; closed by the 2026-07-23 L3 privacy fix) — the
    // BU-lead summary recipient is now resolved by IBuLeadResolver → RoleGrantBuLeadResolver, which anchors
    // ONLY on an explicit BU-scoped OrgRole.BuDelegate grant (the same structural anchor
    // AssessmentReads.ClassifyReader promotes to SeamRole.BuLead), NOT HierarchyRoleResolver's depth-from-root
    // label. The Q-014 mislabel VECTOR still exists in HierarchyRoleResolver — this fixture proves it is
    // still latent — but the recipient port no longer consumes it, so it can no longer leak.
    //
    // This fixture engineers exactly the "not a uniform-depth tree" shape HierarchyRoleResolver's own doc
    // warns about: ROGUE sits at depth 3, is homed in BU_A, and manages a report in a COMPLETELY UNRELATED
    // BU (BU_C) — nothing about ROGUE's actual reporting line touches BU_A's real team. HierarchyRoleResolver
    // therefore STILL depth-labels ROGUE "BU_A's BU Lead" (Premise 1a) — yet IBuLeadResolver refuses to
    // surface ROGUE because ROGUE holds no BuDelegate grant over BU_A (Premise 1b). Proven independently
    // against AssessmentReads.AuthorizeIndividualRead: ROGUE is DENIED a direct read of the very subject
    // (EMP_A1) a BU-lead summary would disclose a count about (Premise 2) — so were the mislabel ever
    // surfaced it would be a genuine leak, which is exactly why the port must (and now does) refuse it.
    //
    // The guard: run the escalation job and assert ROGUE receives NOTHING. Passes under the fix; would fail
    // instantly if the recipient selection ever regressed back to the depth label.
    // =====================================================================================================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task BU_lead_summary_must_never_reach_a_hierarchy_mislabeled_person_with_no_structural_entitlement_to_the_BU()
    {
        await _factory.ResetAsync();
        _factory.Emails.Inner = new RecordingEmailSender();

        var bus = new[]
        {
            Snap.Bu("BU_A", group: "Group A", portfolio: "Portfolio 1"),
            Snap.Bu("BU_C", group: "Group A", portfolio: "Portfolio 1"), // unrelated BU for ROGUE's own report
        };
        var people = new[]
        {
            Snap.Person("ADMIN", "BU_A"),
            // The REAL chain to the non-responder: EXEC (root) --direct--> MGR_A --direct--> EMP_A1.
            // Neither EXEC nor MGR_A sits at depth 3 (EXEC=0, MGR_A=1), so neither is ever mislabeled
            // "BU Lead" — this fixture deliberately leaves BU_A's true leadership tier THIN so ROGUE is
            // the ONLY depth-3/home-BU_A candidate (no first-wins race to reason about).
            Snap.Person("EXEC", "BU_A"),
            Snap.Person("MGR_A", "BU_A", managerExternalRef: "EXEC"),
            Snap.Person("EMP_A1", "BU_A", managerExternalRef: "MGR_A", name: "Nonresponder Norm"),
            // ROGUE's chain: EXEC -> RA -> RB -> ROGUE (depth 3), homed in BU_A but managing a report in
            // BU_C — structurally unconnected to BU_A's real team.
            Snap.Person("RA", "BU_A", managerExternalRef: "EXEC"),
            Snap.Person("RB", "BU_A", managerExternalRef: "RA"),
            Snap.Person("ROGUE", "BU_A", managerExternalRef: "RB", name: "Rogue Outsider"),
            Snap.Person("ROGUE_REPORT", "BU_C", managerExternalRef: "ROGUE"),
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
        var framework = await (await admin.PostAsync("/api/admin/frameworks", null))
            .Content.ReadFromJsonAsync<FrameworkSeedResult>();
        var created = await admin.PostAsJsonAsync("/api/cycles",
            new CreateCycleRequest(framework!.VersionId, "2026-08", true));
        var cycle = await created.Content.ReadFromJsonAsync<CycleResponse>();
        await admin.PostAsync($"/api/cycles/{cycle!.Id}/open", null);

        // Everyone except EMP_A1 submits, so BU_A's non-responder set is exactly {EMP_A1} — isolating the
        // signal to one clean "Team led by MGR_A: 1 not yet submitted" line rather than a noisy fixture.
        foreach (var externalRef in new[] { "EXEC", "MGR_A", "RA", "RB", "ROGUE", "ROGUE_REPORT" })
        {
            await SubmitSelfAsync(externalRef);
        }

        // Premise check 1a (the VECTOR is still real): HierarchyRoleResolver's depth-from-root label STILL
        // mislabels ROGUE — not MGR_A or anyone in BU_A's real chain — as BU_A's BU Lead. The Q-014 mislabel
        // the old recipient port consumed has NOT been fixed away; it lives on in the resolver, so the guard
        // below is proving a live gap is closed, not testing against a vector that no longer exists.
        using (var scope = _factory.NewScope())
        {
            var hierarchy = scope.ServiceProvider.GetRequiredService<HierarchyRoleResolver>();
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            var buAId = (await db.BusinessUnits.SingleAsync(b => b.Code == "BU_A")).Id;
            var rogueId = await PersonIdAsync("ROGUE");
            var roles = await hierarchy.ResolveAllAsync();
            Assert.Equal<Guid?>(buAId, roles[rogueId].BuLeadOfBusinessUnitId); // ROGUE is STILL the depth-3 "BU_A lead"
        }

        // Premise check 1b (the PORT refuses to surface it): IBuLeadResolver — the recipient port the
        // notification actually consumes, now BU-anchored on a BuDelegate grant — does NOT surface ROGUE for
        // BU_A. No BuDelegate grant over BU_A exists in this fixture, so BU_A has no resolvable lead at all
        // (absent from the map → the caller's "skip silently but count" fallback fires) and ROGUE appears
        // nowhere in the recipient set. This is precisely the gap the L3 fix closes.
        using (var scope = _factory.NewScope())
        {
            var buLeadResolver = scope.ServiceProvider.GetRequiredService<IBuLeadResolver>();
            var map = await buLeadResolver.ResolveBuLeadsByBusinessUnitAsync();
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            var buAId = (await db.BusinessUnits.SingleAsync(b => b.Code == "BU_A")).Id;
            var rogueId = await PersonIdAsync("ROGUE");
            Assert.False(map.ContainsKey(buAId)); // BU_A has no BU-anchored (seam-entitled) lead → not mislabeled
            Assert.DoesNotContain(rogueId, map.Values); // ROGUE is never a BU-lead recipient anywhere
        }

        // Premise check 2: ROGUE is NOT structurally entitled to EMP_A1's individual data under the
        // seam's own gate — the same gate the manager escalation (correctly) reuses but the BU-lead
        // summary does not. This is what makes the disclosure below a genuine violation, not a semantic
        // quibble about who "counts" as a BU lead.
        using (var scope = _factory.NewScope())
        {
            var reads = scope.ServiceProvider.GetRequiredService<Hap.Api.Authorization.AssessmentReads>();
            var graphLoader = scope.ServiceProvider.GetRequiredService<Hap.Api.Authorization.OrgGraphLoader>();
            var graph = await graphLoader.LoadAsync();
            var rogueId = await PersonIdAsync("ROGUE");
            var emp1Id = await PersonIdAsync("EMP_A1");
            var decision = reads.AuthorizeIndividualRead(graph, Hap.Api.Authorization.CallerContext.Ungranted(rogueId), emp1Id);
            Assert.False(decision.Allowed); // ROGUE is denied a direct read of the very subject the summary discloses a count about
        }

        // The regression guard: run the escalation job and assert ROGUE receives NOTHING from it. As
        // shipped, this assertion FAILS — ROGUE, structurally unrelated to BU_A's real team, receives
        // BU_A's non-responder summary (a report's aggregated participation status) despite having zero
        // chain relationship to EMP_A1/MGR_A and no BuDelegate grant over BU_A.
        await RunCycleJobAsync(await AsOfDaysBeforeCloseAsync(3));

        var rogueEmail = await PersonEmailAsync("ROGUE");
        var leaked = Recorder.Sent.SingleOrDefault(m => m.To.Contains(rogueEmail) && m.Subject.Contains("by team"));
        Assert.Null(leaked); // must be null once fixed — currently a real message with BU_A's team/counts
    }

    // =====================================================================================================
    // §9.3(b) — the manager-escalation capability gate (HasIndividualReadCapability) is already proven
    // against a Platform Admin reviewer-of-record (CycleReminderJobTests). Extend the sweep to the other
    // two capability-stripped explicit-grant shapes named in the brief: HIG Executive and Group-Viewer.
    // =====================================================================================================

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Manager_escalation_gate_denies_a_HIG_Executive_reviewer_of_record()
    {
        await SeedCapabilityGateFixtureAsync();

        await GrantAsync("EXEC_REVIEWER", OrgRole.HigExecutive);

        var result = await RunCycleJobAsync(await AsOfDaysBeforeCloseAsync(3));
        Assert.True(result.ManagerEscalationsSent >= 0); // sanity: run completed

        var execEmail = await PersonEmailAsync("EXEC_REVIEWER");
        Assert.DoesNotContain(Recorder.Sent,
            m => m.To.Contains(execEmail) && m.Subject.Contains("Team members with an outstanding"));
        Assert.DoesNotContain(Recorder.Sent,
            m => m.Subject.Contains("Team members with an outstanding") && m.Body.Contains("Report Solo"));
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Manager_escalation_gate_denies_a_GroupViewer_reviewer_of_record()
    {
        await SeedCapabilityGateFixtureAsync();

        await GrantAsync("EXEC_REVIEWER", OrgRole.GroupViewer);

        await RunCycleJobAsync(await AsOfDaysBeforeCloseAsync(3));

        var execEmail = await PersonEmailAsync("EXEC_REVIEWER");
        Assert.DoesNotContain(Recorder.Sent,
            m => m.To.Contains(execEmail) && m.Subject.Contains("Team members with an outstanding"));
        Assert.DoesNotContain(Recorder.Sent,
            m => m.Subject.Contains("Team members with an outstanding") && m.Body.Contains("Report Solo"));
    }

    /// <summary>Minimal fixture: EXEC_REVIEWER is REPORT_SOLO's literal direct manager (so absent any
    /// capability-stripping grant they'd be a plain Manager and get the escalation) — the caller then
    /// layers an explicit grant onto EXEC_REVIEWER via <see cref="GrantAsync"/> in each test.</summary>
    private async Task SeedCapabilityGateFixtureAsync()
    {
        await _factory.ResetAsync();
        _factory.Emails.Inner = new RecordingEmailSender();

        var bus = new[] { Snap.Bu("BU_G") };
        var people = new[]
        {
            Snap.Person("ADMIN", "BU_G"),
            Snap.Person("EXEC_REVIEWER", "BU_G"),
            Snap.Person("REPORT_SOLO", "BU_G", managerExternalRef: "EXEC_REVIEWER", name: "Report Solo"),
        };
        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(bus, people));
        using (var scope = _factory.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            (await db.BusinessUnits.SingleAsync(b => b.Code == "BU_G")).SetOnboarded(true);
            await db.SaveChangesAsync();
        }
        _factory.SeedUsers.Inner = new StubSeedUserSource(new[]
        {
            Snap.SeedUser("ADMIN", role: "Platform Admin", buCode: "BU_G"),
            Snap.SeedUser("EXEC_REVIEWER", role: "Individual", buCode: "BU_G"),
            Snap.SeedUser("REPORT_SOLO", role: "Individual", buCode: "BU_G"),
        });

        var admin = _factory.CreateClient();
        await HapApiFactory.SignInAsync(admin, "ADMIN");
        var framework = await (await admin.PostAsync("/api/admin/frameworks", null))
            .Content.ReadFromJsonAsync<FrameworkSeedResult>();
        var created = await admin.PostAsJsonAsync("/api/cycles",
            new CreateCycleRequest(framework!.VersionId, "2026-08", true));
        var cycle = await created.Content.ReadFromJsonAsync<CycleResponse>();
        await admin.PostAsync($"/api/cycles/{cycle!.Id}/open", null);
    }

    // =====================================================================================================
    // §9.3(d) — recipient targeting: a contractor is excluded from the cycle (FR-005) and must never be a
    // reminder RECIPIENT (never invited) nor appear as a non-responder NAME in anyone else's escalation.
    // =====================================================================================================

    [Fact]
    public async Task A_contractor_excluded_person_receives_no_reminder_and_is_never_named_in_an_escalation()
    {
        await _factory.ResetAsync();
        _factory.Emails.Inner = new RecordingEmailSender();

        var bus = new[] { Snap.Bu("BU_X") };
        var people = new[]
        {
            Snap.Person("ADMIN", "BU_X"),
            Snap.Person("MGR_X", "BU_X"),
            Snap.Person("CONTRACTOR_X", "BU_X", managerExternalRef: "MGR_X",
                employeeType: "Contractor", name: "Con Tractor"),
        };
        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(bus, people));
        using (var scope = _factory.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            (await db.BusinessUnits.SingleAsync(b => b.Code == "BU_X")).SetOnboarded(true);
            await db.SaveChangesAsync();
        }
        _factory.SeedUsers.Inner = new StubSeedUserSource(new[]
        {
            Snap.SeedUser("ADMIN", role: "Platform Admin", buCode: "BU_X"),
            Snap.SeedUser("MGR_X", role: "Individual", buCode: "BU_X"),
        });

        var admin = _factory.CreateClient();
        await HapApiFactory.SignInAsync(admin, "ADMIN");
        var framework = await (await admin.PostAsync("/api/admin/frameworks", null))
            .Content.ReadFromJsonAsync<FrameworkSeedResult>();
        var created = await admin.PostAsJsonAsync("/api/cycles",
            // ContractorExclusionEnabled = true
            new CreateCycleRequest(framework!.VersionId, "2026-08", true));
        var cycle = await created.Content.ReadFromJsonAsync<CycleResponse>();
        await admin.PostAsync($"/api/cycles/{cycle!.Id}/open", null);

        // Premise: the contractor's invitation row IS excluded.
        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            var contractorId = await PersonIdAsync("CONTRACTOR_X");
            var invitation = await db.CycleInvitations.SingleAsync(i => i.PersonId == contractorId);
            Assert.True(invitation.Excluded);
        }

        await RunCycleJobAsync(await AsOfDaysBeforeCloseAsync(7));
        await RunCycleJobAsync(await AsOfDaysBeforeCloseAsync(3));

        var contractorEmail = await PersonEmailAsync("CONTRACTOR_X");
        Assert.DoesNotContain(Recorder.Sent, m => m.To.Contains(contractorEmail)); // no reminder ever sent to them
        Assert.DoesNotContain(Recorder.Sent, m => m.Body.Contains("Con Tractor")); // never named in anyone's escalation
    }

    // =====================================================================================================
    // §9.3(d) REGRESSION GUARD (was BLOCKING Finding 2; closed by the same 2026-07-23 L3 privacy fix) —
    // NotificationJobService.SendBuLeadEscalationsAsync (FR-037, weekly-update-discipline BU-Lead escalation)
    // shares the ONE recipient port (IBuLeadResolver) with the FR-061 summary above, so the fix — anchoring
    // the recipient on a BU-scoped BuDelegate grant instead of HierarchyRoleResolver's depth label — closes
    // both consumers by construction. This is Register/Initiative data, not individual assessment data, so
    // it does NOT trip Art. VI's "individual assessment data" framing; it is not tagged PrivacyReporting but
    // IS a regression guard: the mislabel vector is still latent in HierarchyRoleResolver (Premise 1a) yet
    // the port refuses to surface ROGUE (Premise 1b), so the escalation reaches no stranger.
    // =====================================================================================================

    [Fact]
    public async Task FR037_BU_lead_escalation_must_never_reach_a_hierarchy_mislabeled_person_with_no_relationship_to_the_BU()
    {
        await _factory.ResetAsync();
        _factory.Emails.Inner = new RecordingEmailSender();

        var bus = new[]
        {
            Snap.Bu("BU_A", group: "Group A", portfolio: "Portfolio 1"),
            Snap.Bu("BU_C", group: "Group A", portfolio: "Portfolio 1"),
        };
        var people = new[]
        {
            Snap.Person("ADMIN", "BU_A"),
            Snap.Person("EXEC", "BU_A"),
            Snap.Person("OWNER_A", "BU_A", managerExternalRef: "EXEC", name: "Initiative Owner"),
            Snap.Person("RA", "BU_A", managerExternalRef: "EXEC"),
            Snap.Person("RB", "BU_A", managerExternalRef: "RA"),
            Snap.Person("ROGUE", "BU_A", managerExternalRef: "RB", name: "Rogue Outsider"),
            Snap.Person("ROGUE_REPORT", "BU_C", managerExternalRef: "ROGUE"),
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
            await scope.ServiceProvider.GetRequiredService<HarrisTaxonomySeeder>().SeedAsync();
        }
        var seed = new List<SeedUserRecord> { Snap.SeedUser("ADMIN", role: "Platform Admin", buCode: "BU_A") };
        seed.AddRange(people
            .Where(p => p.ExternalRef != "ADMIN")
            .Select(p => Snap.SeedUser(p.ExternalRef, role: "Individual", buCode: p.BuCode)));
        _factory.SeedUsers.Inner = new StubSeedUserSource(seed);

        Guid buAId, ownerId, categoryId;
        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            buAId = (await db.BusinessUnits.SingleAsync(b => b.Code == "BU_A")).Id;
            ownerId = (await db.People.SingleAsync(p => p.ExternalRef == "OWNER_A")).Id;
            categoryId = (await db.HarrisCategories.FirstAsync()).Id;

            var now = DateTime.UtcNow;
            var initiative = Initiative.Create(
                businessUnitId: buAId, name: "Stale Rollout", description: null, sponsorPersonId: null,
                ownerPersonId: ownerId, createdByPersonId: ownerId, categoryId: categoryId, aiDlcLevel: 1,
                functionsAffected: null, dimensionsAdvanced: null, customersInProduction: null, riskTier: RiskTier.Low);
            initiative.AdvanceStage(InitiativeStage.Evaluation);
            initiative.PostWeeklyUpdate(RagStatus.OnTrack, now.AddDays(-20)); // >14d → BU-lead escalation
            db.Initiatives.Add(initiative);
            await db.SaveChangesAsync();
        }

        // Premise 1a (vector still latent): HierarchyRoleResolver STILL depth-labels ROGUE — not OWNER_A's
        // real chain (EXEC) — as BU_A's BU Lead.
        using (var scope = _factory.NewScope())
        {
            var hierarchy = scope.ServiceProvider.GetRequiredService<HierarchyRoleResolver>();
            var rogueId = await PersonIdAsync("ROGUE");
            var roles = await hierarchy.ResolveAllAsync();
            Assert.Equal<Guid?>(buAId, roles[rogueId].BuLeadOfBusinessUnitId);
        }

        // Premise 1b (port refuses to surface it): the recipient port the escalation actually consumes
        // (IBuLeadResolver, BU-anchored on a BuDelegate grant) does NOT surface ROGUE — no BuDelegate grant
        // over BU_A exists, so BU_A is absent from the map and ROGUE is never a recipient.
        using (var scope = _factory.NewScope())
        {
            var buLeadResolver = scope.ServiceProvider.GetRequiredService<IBuLeadResolver>();
            var map = await buLeadResolver.ResolveBuLeadsByBusinessUnitAsync();
            var rogueId = await PersonIdAsync("ROGUE");
            Assert.False(map.ContainsKey(buAId));
            Assert.DoesNotContain(rogueId, map.Values);
        }

        using (var scope = _factory.NewScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<NotificationJobService>();
            await service.RunWeeklyUpdateNagsAsync(DateTime.UtcNow);
        }

        var rogueEmail = await PersonEmailAsync("ROGUE");
        // Regression guard: currently FAILS — ROGUE receives the BU-lead escalation about OWNER_A's
        // overdue initiative today, despite having no relationship to BU_A's real chain.
        Assert.DoesNotContain(Recorder.Sent, m => m.To.Contains(rogueEmail));
    }

    // =====================================================================================================
    // TEETH — a recipient port that surfaces NOBODY trivially passes every "nobody leaks" guard above, so
    // the guards alone can't prove the BU-anchored mechanism actually works. This positive/negative pair
    // rules that out: the anchor must be the SPECIFIC BU (not "any BuDelegate grant anywhere"), and a
    // genuinely BU-anchored delegate MUST receive the summary (the path is live, not dead).
    // =====================================================================================================

    /// <summary>NEGATIVE mis-anchor: ROGUE holds a BuDelegate grant, but over the WRONG BU (BU_C). A
    /// correctly BU-anchored resolver must give ROGUE zero entitlement to BU_A's data — so ROGUE receives
    /// no BU_A team summary. (A resolver that treated "holds any BuDelegate grant" as leadership of BU_A
    /// would fail here.)</summary>
    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task FR061_a_BuDelegate_grant_over_the_wrong_bu_confers_no_entitlement_to_another_bu()
    {
        var ids = await SeedRogueCycleFixtureAsync();

        await GrantAsync("ROGUE", OrgRole.BuDelegate, "BU_C"); // mis-anchor: BU_C, not BU_A

        // Premise: ROGUE now IS BU_C's resolvable lead (the grant is real and BU-anchored) — so this test
        // is not vacuously true; ROGUE holds a live BuDelegate grant, just over the wrong BU.
        using (var scope = _factory.NewScope())
        {
            var buLeadResolver = scope.ServiceProvider.GetRequiredService<IBuLeadResolver>();
            var map = await buLeadResolver.ResolveBuLeadsByBusinessUnitAsync();
            Assert.True(map.TryGetValue(ids.BuCId, out var bucLead) && bucLead == ids.RogueId); // BU_C → ROGUE
            Assert.False(map.ContainsKey(ids.BuAId)); // BU_A still has NO resolvable lead
        }

        await RunCycleJobAsync(await AsOfDaysBeforeCloseAsync(3));

        var rogueEmail = await PersonEmailAsync("ROGUE");
        // ROGUE must receive nothing about BU_A. (Every non-BU_A person submitted, so BU_C has no
        // non-responders either — a correct mechanism sends ROGUE no team summary at all.)
        Assert.DoesNotContain(Recorder.Sent, m => m.To.Contains(rogueEmail) && m.Subject.Contains("by team"));
        Assert.DoesNotContain(Recorder.Sent, m => m.To.Contains(rogueEmail) && m.Subject.Contains("BU_A BU"));
    }

    /// <summary>POSITIVE: RA — homed in BU_A and in its real chain — is granted an explicit BuDelegate over
    /// BU_A (the same anchor the seam promotes to SeamRole.BuLead). RA MUST receive BU_A's per-team count
    /// summary, proving the recipient path is live rather than a resolver that safely returns nobody.</summary>
    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task FR061_a_BuDelegate_anchored_on_the_bu_receives_that_bus_summary()
    {
        var ids = await SeedRogueCycleFixtureAsync();

        await GrantAsync("RA", OrgRole.BuDelegate, "BU_A"); // legitimately BU-anchored lead of BU_A

        using (var scope = _factory.NewScope())
        {
            var buLeadResolver = scope.ServiceProvider.GetRequiredService<IBuLeadResolver>();
            var map = await buLeadResolver.ResolveBuLeadsByBusinessUnitAsync();
            var raId = await PersonIdAsync("RA");
            Assert.True(map.TryGetValue(ids.BuAId, out var lead) && lead == raId); // BU_A → RA now resolvable
        }

        await RunCycleJobAsync(await AsOfDaysBeforeCloseAsync(3));

        var raEmail = await PersonEmailAsync("RA");
        var buASummary = Recorder.Sent.SingleOrDefault(
            m => m.To.Contains(raEmail) && m.Subject.Contains("by team") && m.Subject.Contains("BU_A BU"));
        Assert.NotNull(buASummary); // the BU-anchored delegate DOES receive BU_A's summary — path is live
        Assert.Contains("not yet submitted", buASummary!.Body); // a per-team count line, naming no individual
        Assert.DoesNotContain("Nonresponder Norm", buASummary.Body); // still names no individual (count-only)
    }

    /// <summary>Seeds the FR-061 "rogue depth-3 mislabel" cycle fixture used by the guard + teeth tests:
    /// a non-uniform tree where ROGUE (depth 3, homed BU_A, managing a report in BU_C) is HierarchyRoleResolver's
    /// unique BU_A "lead" label, an open cycle, and every invited person EXCEPT EMP_A1 submitted — so BU_A's
    /// non-responder set is exactly {EMP_A1}. No BuDelegate grants are seeded (callers add their own).</summary>
    private async Task<(Guid BuAId, Guid BuCId, Guid RogueId, Guid Emp1Id, Guid MgrAId)> SeedRogueCycleFixtureAsync()
    {
        await _factory.ResetAsync();
        _factory.Emails.Inner = new RecordingEmailSender();

        var bus = new[]
        {
            Snap.Bu("BU_A", group: "Group A", portfolio: "Portfolio 1"),
            Snap.Bu("BU_C", group: "Group A", portfolio: "Portfolio 1"),
        };
        var people = new[]
        {
            Snap.Person("ADMIN", "BU_A"),
            Snap.Person("EXEC", "BU_A"),
            Snap.Person("MGR_A", "BU_A", managerExternalRef: "EXEC"),
            Snap.Person("EMP_A1", "BU_A", managerExternalRef: "MGR_A", name: "Nonresponder Norm"),
            Snap.Person("RA", "BU_A", managerExternalRef: "EXEC"),
            Snap.Person("RB", "BU_A", managerExternalRef: "RA"),
            Snap.Person("ROGUE", "BU_A", managerExternalRef: "RB", name: "Rogue Outsider"),
            Snap.Person("ROGUE_REPORT", "BU_C", managerExternalRef: "ROGUE"),
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
        var framework = await (await admin.PostAsync("/api/admin/frameworks", null))
            .Content.ReadFromJsonAsync<FrameworkSeedResult>();
        var created = await admin.PostAsJsonAsync("/api/cycles",
            new CreateCycleRequest(framework!.VersionId, "2026-08", true));
        var cycle = await created.Content.ReadFromJsonAsync<CycleResponse>();
        await admin.PostAsync($"/api/cycles/{cycle!.Id}/open", null);

        foreach (var externalRef in new[] { "EXEC", "MGR_A", "RA", "RB", "ROGUE", "ROGUE_REPORT" })
        {
            await SubmitSelfAsync(externalRef);
        }

        using var idScope = _factory.NewScope();
        var idDb = idScope.ServiceProvider.GetRequiredService<HapDbContext>();
        return (
            (await idDb.BusinessUnits.SingleAsync(b => b.Code == "BU_A")).Id,
            (await idDb.BusinessUnits.SingleAsync(b => b.Code == "BU_C")).Id,
            await PersonIdAsync("ROGUE"),
            await PersonIdAsync("EMP_A1"),
            await PersonIdAsync("MGR_A"));
    }

    // =====================================================================================================
    // L3 RE-REVIEW ROUND 2 (2026-07-23) — the earlier fix bound the BU-lead recipient to ONLY the seam's
    // BuDelegate ANCHOR conjunct, dropping the seam's other two conjuncts. Both close a real leak the panel
    // (code-reviewer + red-team, converged) proved reachable, because role_grants is append-only with no
    // uniqueness constraint and LocalDevProvider accumulates roles per person:
    //   (1) PRECEDENCE — a candidate co-holding a capability-stripping grant (HigExec / PlatformAdmin /
    //       GroupViewer) is DENIED by AssessmentReads.AuthorizeIndividualRead (ClassifyReader ranks those
    //       above BuDelegate and strips individual-read capability) yet was still SELECTED by the resolver →
    //       they'd receive the FR-061 per-team summary (an n=1 count, also defeating N<4).
    //   (2) ELIGIBILITY — the seam's ReaderEligible conjunct (active + non-contractor) was enforced nowhere
    //       on the notification path; deactivation is the only entitlement-ender (grants are append-only), so
    //       a deactivated/contractor delegate still got mailed.
    // Fix: the FR-061 consumer (CycleReminderJob, Hap.Api) now gates each resolved candidate through the ONE
    // seam authority (AuthorizeIndividualRead over the candidate's full grant set); the FR-037 consumer
    // (NotificationJobService, Hap.Infrastructure) applies the eligibility conjunct over plain Person
    // attributes. These are the Dev-attributed regression guards for round 2.
    // =====================================================================================================

    /// <summary>(1) PRECEDENCE — a BuDelegate(BU_A) holder who ALSO holds a capability-stripping grant is
    /// denied by the seam, so must receive NO BU-lead summary even though the resolver still surfaces them on
    /// the BuDelegate anchor. Swept across all three stripping grants (GroupViewer / HIG Executive / Platform
    /// Admin). The red-team supplied the GroupViewer fixture; the other two share the ClassifyReader
    /// precedence that strips capability.</summary>
    [Theory]
    [Trait("Category", "PrivacyReporting")]
    [InlineData(OrgRole.GroupViewer)]
    [InlineData(OrgRole.HigExecutive)]
    [InlineData(OrgRole.PlatformAdmin)]
    public async Task FR061_a_BuDelegate_who_also_holds_a_capability_stripping_grant_must_not_receive_the_summary(
        OrgRole strippingRole)
    {
        var ids = await SeedRogueCycleFixtureAsync();

        // RA is homed in BU_A and in its real chain. Give RA a BU_A-anchored BuDelegate grant (so the
        // resolver surfaces RA as BU_A's candidate lead) AND a capability-stripping grant. The seam classifies
        // RA by the HIGHER role and strips individual-read capability — so the seam would DENY RA — but the
        // resolver anchors only on the BuDelegate grant and still surfaces RA.
        await GrantAsync("RA", OrgRole.BuDelegate, "BU_A");
        await GrantAsync("RA", strippingRole);

        // Premise 1 (the resolver STILL surfaces RA — the leak vector is live, not vacuous): RA is BU_A's
        // sole BuDelegate-grant holder, so the candidate-producing port returns RA for BU_A.
        using (var scope = _factory.NewScope())
        {
            var buLeadResolver = scope.ServiceProvider.GetRequiredService<IBuLeadResolver>();
            var map = await buLeadResolver.ResolveBuLeadsByBusinessUnitAsync();
            var raId = await PersonIdAsync("RA");
            Assert.True(map.TryGetValue(ids.BuAId, out var lead) && lead == raId);
        }

        // Premise 2 (the seam DENIES RA): over RA's FULL grant set, AuthorizeIndividualRead refuses a direct
        // read of EMP_A1 — the very subject the BU_A summary discloses a count about — because the co-held
        // stripping grant removes individual-read capability. Surfacing RA would therefore be a genuine leak.
        using (var scope = _factory.NewScope())
        {
            var reads = scope.ServiceProvider.GetRequiredService<Hap.Api.Authorization.AssessmentReads>();
            var graphLoader = scope.ServiceProvider.GetRequiredService<Hap.Api.Authorization.OrgGraphLoader>();
            var graph = await graphLoader.LoadAsync();
            var caller = await CallerContextAsync("RA");
            Assert.False(reads.AuthorizeIndividualRead(graph, caller, ids.Emp1Id).Allowed);
        }

        await RunCycleJobAsync(await AsOfDaysBeforeCloseAsync(3));

        var raEmail = await PersonEmailAsync("RA");
        // The regression guard: RA must receive NO BU_A team summary. Pre-fix this FAILS — RA receives
        // "Team led by MGR_A: 1 not yet submitted" (a seam-denied reader getting an n=1 assessment count).
        Assert.DoesNotContain(Recorder.Sent, m => m.To.Contains(raEmail) && m.Subject.Contains("by team"));
    }

    /// <summary>(2) ELIGIBILITY — a DEACTIVATED BuDelegate(BU_A) holder must receive no FR-061 summary. Grants
    /// are append-only (no revocation), so deactivation is the only entitlement-ender; the resolver still
    /// surfaces the delegate, and only the consumer's ReaderEligible-parity gate stops the mail.</summary>
    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task FR061_a_deactivated_BuDelegate_must_not_receive_the_summary()
    {
        var ids = await SeedDelegateEligibilityCycleFixtureAsync(delegateEmployeeType: "Employee", delegateActive: false);

        // Premise: the resolver still surfaces the (deactivated) delegate — the BuDelegate grant persists.
        using (var scope = _factory.NewScope())
        {
            var map = await scope.ServiceProvider.GetRequiredService<IBuLeadResolver>().ResolveBuLeadsByBusinessUnitAsync();
            Assert.True(map.TryGetValue(ids.BuAId, out var lead) && lead == ids.DelegateId);
        }

        await RunCycleJobAsync(await AsOfDaysBeforeCloseAsync(3));

        var delegateEmail = await PersonEmailAsync("DELEGATE");
        Assert.DoesNotContain(Recorder.Sent, m => m.To.Contains(delegateEmail) && m.Subject.Contains("by team"));
    }

    /// <summary>(2) ELIGIBILITY — a CONTRACTOR BuDelegate(BU_A) holder must receive no FR-061 summary under
    /// the seam's Q-006 Restrictive contractor policy (a contractor gets no individual-score access at all).
    /// The resolver surfaces them; the consumer's seam gate (via ReaderEligible) refuses the send.</summary>
    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task FR061_a_contractor_BuDelegate_must_not_receive_the_summary()
    {
        var ids = await SeedDelegateEligibilityCycleFixtureAsync(delegateEmployeeType: "Contractor", delegateActive: true);

        using (var scope = _factory.NewScope())
        {
            var map = await scope.ServiceProvider.GetRequiredService<IBuLeadResolver>().ResolveBuLeadsByBusinessUnitAsync();
            Assert.True(map.TryGetValue(ids.BuAId, out var lead) && lead == ids.DelegateId);
        }

        await RunCycleJobAsync(await AsOfDaysBeforeCloseAsync(3));

        var delegateEmail = await PersonEmailAsync("DELEGATE");
        Assert.DoesNotContain(Recorder.Sent, m => m.To.Contains(delegateEmail) && m.Subject.Contains("by team"));
    }

    /// <summary>(2) ELIGIBILITY, FR-037 sibling (Register data, not PrivacyReporting) — a DEACTIVATED
    /// BuDelegate(BU_A) holder must receive no weekly-update BU-lead escalation. Same append-only-grant
    /// premise; the eligibility gate lives in NotificationJobService (Infrastructure), over plain Person
    /// attributes, since that layer must not import the assessment seam.</summary>
    [Fact]
    public async Task FR037_a_deactivated_BuDelegate_must_not_receive_the_escalation()
    {
        await _factory.ResetAsync();
        _factory.Emails.Inner = new RecordingEmailSender();

        var bus = new[] { Snap.Bu("BU_A") };
        var people = new[]
        {
            Snap.Person("ADMIN", "BU_A"),
            Snap.Person("EXEC", "BU_A"),
            Snap.Person("OWNER_A", "BU_A", managerExternalRef: "EXEC", name: "Initiative Owner"),
            Snap.Person("DELEGATE", "BU_A", managerExternalRef: "EXEC", isActive: false, name: "Departed Delegate"),
        };
        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(bus, people));
        using (var scope = _factory.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            (await db.BusinessUnits.SingleAsync(b => b.Code == "BU_A")).SetOnboarded(true);
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<HarrisTaxonomySeeder>().SeedAsync();
        }
        _factory.SeedUsers.Inner = new StubSeedUserSource(new[] { Snap.SeedUser("ADMIN", role: "Platform Admin", buCode: "BU_A") });

        Guid buAId;
        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            buAId = (await db.BusinessUnits.SingleAsync(b => b.Code == "BU_A")).Id;
            var ownerId = (await db.People.SingleAsync(p => p.ExternalRef == "OWNER_A")).Id;
            var categoryId = (await db.HarrisCategories.FirstAsync()).Id;
            var initiative = Initiative.Create(
                businessUnitId: buAId, name: "Stale Rollout", description: null, sponsorPersonId: null,
                ownerPersonId: ownerId, createdByPersonId: ownerId, categoryId: categoryId, aiDlcLevel: 1,
                functionsAffected: null, dimensionsAdvanced: null, customersInProduction: null, riskTier: RiskTier.Low);
            initiative.AdvanceStage(InitiativeStage.Evaluation);
            initiative.PostWeeklyUpdate(RagStatus.OnTrack, DateTime.UtcNow.AddDays(-20)); // >14d → BU-lead escalation
            db.Initiatives.Add(initiative);
            await db.SaveChangesAsync();
        }

        await GrantAsync("DELEGATE", OrgRole.BuDelegate, "BU_A");

        // Premise: the resolver surfaces the deactivated delegate as BU_A's candidate (grant persists).
        using (var scope = _factory.NewScope())
        {
            var map = await scope.ServiceProvider.GetRequiredService<IBuLeadResolver>().ResolveBuLeadsByBusinessUnitAsync();
            var delegateId = await PersonIdAsync("DELEGATE");
            Assert.True(map.TryGetValue(buAId, out var lead) && lead == delegateId);
        }

        using (var scope = _factory.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<NotificationJobService>().RunWeeklyUpdateNagsAsync(DateTime.UtcNow);
        }

        var delegateEmail = await PersonEmailAsync("DELEGATE");
        // Pre-fix this FAILS — the departed delegate receives BU_A's overdue-initiative escalation.
        Assert.DoesNotContain(Recorder.Sent, m => m.To.Contains(delegateEmail));
    }

    /// <summary>Seeds a minimal open-cycle fixture for the BU-lead ELIGIBILITY guards: BU_A with
    /// EXEC → MGR_A → EMP_A1 (the sole non-responder) plus a DELEGATE homed in BU_A whose employee-type /
    /// active flag the caller sets. EXEC and MGR_A submit; the delegate holds a BU_A-anchored BuDelegate
    /// grant (so the resolver surfaces them) and is NOT invited/submitting — only the consumer's eligibility
    /// gate should keep them from the summary.</summary>
    private async Task<(Guid BuAId, Guid DelegateId, Guid Emp1Id)> SeedDelegateEligibilityCycleFixtureAsync(
        string delegateEmployeeType, bool delegateActive)
    {
        await _factory.ResetAsync();
        _factory.Emails.Inner = new RecordingEmailSender();

        var bus = new[] { Snap.Bu("BU_A") };
        var people = new[]
        {
            Snap.Person("ADMIN", "BU_A"),
            Snap.Person("EXEC", "BU_A"),
            Snap.Person("MGR_A", "BU_A", managerExternalRef: "EXEC"),
            Snap.Person("EMP_A1", "BU_A", managerExternalRef: "MGR_A", name: "Nonresponder Norm"),
            Snap.Person("DELEGATE", "BU_A", managerExternalRef: "EXEC",
                employeeType: delegateEmployeeType, isActive: delegateActive, name: "Ineligible Delegate"),
        };
        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(bus, people));
        using (var scope = _factory.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            (await db.BusinessUnits.SingleAsync(b => b.Code == "BU_A")).SetOnboarded(true);
            await db.SaveChangesAsync();
        }

        var seed = new List<SeedUserRecord> { Snap.SeedUser("ADMIN", role: "Platform Admin", buCode: "BU_A") };
        seed.AddRange(new[] { "EXEC", "MGR_A", "EMP_A1" }
            .Select(r => Snap.SeedUser(r, role: "Individual", buCode: "BU_A")));
        _factory.SeedUsers.Inner = new StubSeedUserSource(seed);

        var admin = _factory.CreateClient();
        await HapApiFactory.SignInAsync(admin, "ADMIN");
        var framework = await (await admin.PostAsync("/api/admin/frameworks", null))
            .Content.ReadFromJsonAsync<FrameworkSeedResult>();
        var created = await admin.PostAsJsonAsync("/api/cycles",
            new CreateCycleRequest(framework!.VersionId, "2026-08", true));
        var cycle = await created.Content.ReadFromJsonAsync<CycleResponse>();
        await admin.PostAsync($"/api/cycles/{cycle!.Id}/open", null);

        // EXEC and MGR_A submit; EMP_A1 does not → BU_A non-responder set = {EMP_A1}.
        foreach (var externalRef in new[] { "EXEC", "MGR_A" })
        {
            await SubmitSelfAsync(externalRef);
        }

        await GrantAsync("DELEGATE", OrgRole.BuDelegate, "BU_A");

        using var idScope = _factory.NewScope();
        var idDb = idScope.ServiceProvider.GetRequiredService<HapDbContext>();
        return (
            (await idDb.BusinessUnits.SingleAsync(b => b.Code == "BU_A")).Id,
            await PersonIdAsync("DELEGATE"),
            await PersonIdAsync("EMP_A1"));
    }

    // =====================================================================================================
    // §9.3(a) — attack the admin/notifications/run OUTPUT itself: the response is a flat
    // Dictionary<string,int> (counts only) by construction, but confirm it directly rather than trusting
    // the shape from reading the source.
    // =====================================================================================================

    [Fact]
    public async Task Admin_notifications_run_response_carries_only_integer_counts_no_names_or_states()
    {
        await _factory.ResetAsync();
        _factory.Emails.Inner = new RecordingEmailSender();
        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(
            new[] { Snap.Bu("BU_Y") },
            new[] { Snap.Person("ADMIN", "BU_Y", name: "Should Not Appear") }));
        using (var scope = _factory.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            (await db.BusinessUnits.SingleAsync(b => b.Code == "BU_Y")).SetOnboarded(true);
            await db.SaveChangesAsync();
        }
        _factory.SeedUsers.Inner = new StubSeedUserSource(new[] { Snap.SeedUser("ADMIN", role: "Platform Admin", buCode: "BU_Y") });

        var admin = _factory.CreateClient();
        await HapApiFactory.SignInAsync(admin, "ADMIN");

        var response = await admin.PostAsync("/api/admin/notifications/run", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("Should Not Appear", raw);
        Assert.DoesNotContain("@synth.local", raw); // no email address ever appears in the counts payload

        var counts = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(raw)!;
        Assert.NotEmpty(counts);
        Assert.All(counts, kv => Assert.Equal(System.Text.Json.JsonValueKind.Number, kv.Value.ValueKind)); // every value is a bare integer count
    }
}
