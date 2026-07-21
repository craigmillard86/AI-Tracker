using System.Net;
using System.Net.Http.Json;
using Hap.Domain.Cycles;
using Hap.Domain.Org;
using Hap.Infrastructure;
using Hap.Infrastructure.Directory;
using Hap.Infrastructure.Frameworks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hap.Api.Tests;

/// <summary>
/// HAP-7 acceptance criteria against the live seeded framework and a hand-built org fixture:
/// invitation generation + counts (FR-003/005), one-Open-per-framework (FR-002), mid-cycle
/// onboarding snapshot immutability (FR-002), forward-only state machine, contractor-exclusion
/// toggle (FR-005), and late-override scope (FR-002). Every test signs in as a bootstrapped
/// Platform Admin fixture for cycle create/open/close (matching the [PA] gate); the late-override
/// scope tests additionally sign in as an ordinary manager fixture to exercise the "own directs
/// only" rule.
///
/// <para>Fixture shape (BU01 onboarded up front, BU02 onboarded later to drive the mid-cycle
/// test): ADMIN, MGR1, EMP1, CONTRACTOR1 in BU01 (MGR1 manages EMP1 + CONTRACTOR1); EMP2 alone in
/// BU02. With BU01 onboarded and default contractor exclusion: 5 active people total, 3 invited
/// (ADMIN/MGR1/EMP1), 1 excluded-Contractor (CONTRACTOR1), 1 excluded-NotOnboarded (EMP2).</para>
/// </summary>
[Collection("hap-db")]
public sealed class CycleEndpointsTests
{
    private readonly HapApiFactory _factory;

    public CycleEndpointsTests(HapApiFactory factory) => _factory = factory;

    private static readonly string DefinitionPath = FrameworkDefinitionLocator.ResolveDefaultPath();

    /// <summary>Resets the DB, syncs the fixture org (BU01 onboarded, BU02 not), seeds the
    /// framework, and returns an admin-authenticated client plus the active FrameworkVersionId.</summary>
    private async Task<(HttpClient Client, Guid FrameworkVersionId)> AdminClientWithFrameworkAsync()
    {
        await _factory.ResetAsync();
        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(
            new[] { Snap.Bu("BU01"), Snap.Bu("BU02") },
            new[]
            {
                Snap.Person("ADMIN", "BU01"),
                Snap.Person("MGR1", "BU01"),
                Snap.Person("EMP1", "BU01", managerExternalRef: "MGR1"),
                Snap.Person("CONTRACTOR1", "BU01", managerExternalRef: "MGR1", employeeType: "Contractor"),
                Snap.Person("EMP2", "BU02"),
            }));
        using (var scope = _factory.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();

            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            var bu01 = await db.BusinessUnits.SingleAsync(b => b.Code == "BU01");
            bu01.SetOnboarded(true);
            await db.SaveChangesAsync();
        }
        _factory.SeedUsers.Inner = new StubSeedUserSource(new[]
        {
            Snap.SeedUser("ADMIN", role: "Platform Admin"),
            Snap.SeedUser("MGR1", role: "Manager"),
            Snap.SeedUser("EMP1", role: "Individual"),
            Snap.SeedUser("CONTRACTOR1", role: "Individual"),
            Snap.SeedUser("EMP2", role: "Individual"),
        });

        var client = _factory.CreateClient();
        await HapApiFactory.SignInAsync(client, "ADMIN");

        var seedResponse = await client.PostAsync("/api/admin/frameworks", content: null);
        var seedResult = await seedResponse.Content.ReadFromJsonAsync<FrameworkSeedResult>();

        return (client, seedResult!.VersionId);
    }

    private async Task<Guid> CreateDraftCycleAsync(
        HttpClient client, Guid frameworkVersionId, string name = "2026-08", bool contractorExclusionEnabled = true)
    {
        var response = await client.PostAsJsonAsync("/api/cycles",
            new CreateCycleRequest(frameworkVersionId, name, contractorExclusionEnabled));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var cycle = await response.Content.ReadFromJsonAsync<CycleResponse>();
        return cycle!.Id;
    }

    [Fact]
    public async Task Open_generates_invitations_with_the_expected_counts_and_per_row_shape()
    {
        var (client, frameworkVersionId) = await AdminClientWithFrameworkAsync();
        var cycleId = await CreateDraftCycleAsync(client, frameworkVersionId);

        var response = await client.PostAsync($"/api/cycles/{cycleId}/open", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CycleOpenResponse>();

        Assert.Equal(5, result!.TotalActivePeople);
        Assert.Equal(3, result.Invited);           // ADMIN, MGR1, EMP1
        Assert.Equal(1, result.ExcludedContractor); // CONTRACTOR1
        Assert.Equal(1, result.ExcludedNotOnboarded); // EMP2 (BU02 not onboarded)
        Assert.Equal("Open", result.Cycle.State);
        Assert.NotNull(result.Cycle.OpensAt);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var invitations = await db.CycleInvitations.Where(i => i.CycleId == cycleId).ToListAsync();
        Assert.Equal(5, invitations.Count);

        var contractor = await db.People.SingleAsync(p => p.ExternalRef == "CONTRACTOR1");
        var contractorRow = invitations.Single(i => i.PersonId == contractor.Id);
        Assert.True(contractorRow.Excluded);
        Assert.Equal(InvitationExclusionReason.Contractor, contractorRow.ExcludedReason);
        Assert.Null(contractorRow.InvitedAt); // "no invitation email row" (no invite fired)

        var emp2 = await db.People.SingleAsync(p => p.ExternalRef == "EMP2");
        var emp2Row = invitations.Single(i => i.PersonId == emp2.Id);
        Assert.True(emp2Row.Excluded);
        Assert.Equal(InvitationExclusionReason.NotOnboarded, emp2Row.ExcludedReason);

        var admin = await db.People.SingleAsync(p => p.ExternalRef == "ADMIN");
        var adminRow = invitations.Single(i => i.PersonId == admin.Id);
        Assert.False(adminRow.Excluded);
        Assert.NotNull(adminRow.InvitedAt);
    }

    [Fact]
    public async Task Opening_a_second_cycle_for_the_same_framework_while_one_is_open_returns_409()
    {
        var (client, frameworkVersionId) = await AdminClientWithFrameworkAsync();
        var firstCycleId = await CreateDraftCycleAsync(client, frameworkVersionId, name: "2026-08");
        await client.PostAsync($"/api/cycles/{firstCycleId}/open", content: null);

        var secondCycleId = await CreateDraftCycleAsync(client, frameworkVersionId, name: "2026-09");
        var response = await client.PostAsync($"/api/cycles/{secondCycleId}/open", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Bu_onboarded_mid_open_cycle_gets_no_invitations_until_the_next_open()
    {
        var (client, frameworkVersionId) = await AdminClientWithFrameworkAsync();

        var cycleAId = await CreateDraftCycleAsync(client, frameworkVersionId, name: "2026-08");
        await client.PostAsync($"/api/cycles/{cycleAId}/open", content: null);

        Guid emp2Id;
        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            emp2Id = (await db.People.SingleAsync(p => p.ExternalRef == "EMP2")).Id;
            var emp2RowAtOpenA = await db.CycleInvitations.SingleAsync(i => i.CycleId == cycleAId && i.PersonId == emp2Id);
            Assert.True(emp2RowAtOpenA.Excluded);
            Assert.Equal(InvitationExclusionReason.NotOnboarded, emp2RowAtOpenA.ExcludedReason);
        }

        // Onboard BU02 while cycle A is still Open — the already-generated snapshot must not change.
        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            var bu02 = await db.BusinessUnits.SingleAsync(b => b.Code == "BU02");
            bu02.SetOnboarded(true);
            await db.SaveChangesAsync();
        }
        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            var stillOnlyOneRow = await db.CycleInvitations.CountAsync(i => i.CycleId == cycleAId && i.PersonId == emp2Id);
            Assert.Equal(1, stillOnlyOneRow); // no retroactive invite/regeneration
            var unchangedRow = await db.CycleInvitations.SingleAsync(i => i.CycleId == cycleAId && i.PersonId == emp2Id);
            Assert.True(unchangedRow.Excluded); // still excluded — the snapshot from open-time is frozen
        }

        await client.PostAsync($"/api/cycles/{cycleAId}/close", content: null);
        var cycleBId = await CreateDraftCycleAsync(client, frameworkVersionId, name: "2026-09");
        await client.PostAsync($"/api/cycles/{cycleBId}/open", content: null);

        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            var emp2RowAtOpenB = await db.CycleInvitations.SingleAsync(i => i.CycleId == cycleBId && i.PersonId == emp2Id);
            Assert.False(emp2RowAtOpenB.Excluded); // now invited — BU02 onboarded before this open
            Assert.NotNull(emp2RowAtOpenB.InvitedAt);
        }
    }

    [Fact]
    public async Task State_machine_rejects_closed_to_open_and_draft_to_closed()
    {
        var (client, frameworkVersionId) = await AdminClientWithFrameworkAsync();

        var cycleId = await CreateDraftCycleAsync(client, frameworkVersionId);
        var draftToClosed = await client.PostAsync($"/api/cycles/{cycleId}/close", content: null);
        Assert.Equal(HttpStatusCode.Conflict, draftToClosed.StatusCode);

        await client.PostAsync($"/api/cycles/{cycleId}/open", content: null);
        await client.PostAsync($"/api/cycles/{cycleId}/close", content: null);

        var closedToOpen = await client.PostAsync($"/api/cycles/{cycleId}/open", content: null);
        Assert.Equal(HttpStatusCode.Conflict, closedToOpen.StatusCode);
    }

    [Fact]
    public async Task Contractor_exclusion_disabled_invites_the_contractor()
    {
        var (client, frameworkVersionId) = await AdminClientWithFrameworkAsync();
        var cycleId = await CreateDraftCycleAsync(client, frameworkVersionId, contractorExclusionEnabled: false);

        var response = await client.PostAsync($"/api/cycles/{cycleId}/open", content: null);
        var result = await response.Content.ReadFromJsonAsync<CycleOpenResponse>();

        Assert.Equal(4, result!.Invited); // ADMIN, MGR1, EMP1, CONTRACTOR1
        Assert.Equal(0, result.ExcludedContractor);
    }

    [Fact]
    public async Task Cadence_name_and_open_close_timestamps_round_trip()
    {
        var (client, frameworkVersionId) = await AdminClientWithFrameworkAsync();
        var cycleId = await CreateDraftCycleAsync(client, frameworkVersionId, name: "2026-08");

        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            var draft = await db.Cycles.SingleAsync(c => c.Id == cycleId);
            Assert.Equal("2026-08", draft.Name);
            Assert.Null(draft.OpensAt);
            Assert.Null(draft.ClosesAt);
        }

        await client.PostAsync($"/api/cycles/{cycleId}/open", content: null);
        var closeResponse = await client.PostAsync($"/api/cycles/{cycleId}/close", content: null);
        var closed = await closeResponse.Content.ReadFromJsonAsync<CycleResponse>();

        Assert.NotNull(closed!.OpensAt);
        Assert.NotNull(closed.ClosesAt);
    }

    [Fact]
    public async Task Late_override_after_close_lets_the_lock_primitive_allow_submission()
    {
        var (client, frameworkVersionId) = await AdminClientWithFrameworkAsync();
        var cycleId = await CreateDraftCycleAsync(client, frameworkVersionId);
        await client.PostAsync($"/api/cycles/{cycleId}/open", content: null);
        await client.PostAsync($"/api/cycles/{cycleId}/close", content: null);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var emp1 = await db.People.SingleAsync(p => p.ExternalRef == "EMP1");
        var closedCycle = await db.Cycles.SingleAsync(c => c.Id == cycleId);

        Assert.False(closedCycle.AllowsSubmission(hasLateOverride: false));

        var overrideResponse = await client.PostAsJsonAsync(
            $"/api/cycles/{cycleId}/late-override", new LateOverrideRequest(emp1.Id));
        Assert.Equal(HttpStatusCode.OK, overrideResponse.StatusCode);
        var grant = await overrideResponse.Content.ReadFromJsonAsync<LateOverrideResponse>();
        Assert.Equal("PlatformAdmin", grant!.GrantedByRole);

        var hasOverride = await db.CycleLateOverrides.AnyAsync(o => o.CycleId == cycleId && o.PersonId == emp1.Id);
        Assert.True(hasOverride);
        Assert.True(closedCycle.AllowsSubmission(hasLateOverride: true));
    }

    [Fact]
    public async Task PlatformAdmin_can_grant_a_late_override_for_any_person()
    {
        var (client, frameworkVersionId) = await AdminClientWithFrameworkAsync();
        var cycleId = await CreateDraftCycleAsync(client, frameworkVersionId);
        await client.PostAsync($"/api/cycles/{cycleId}/open", content: null);
        await client.PostAsync($"/api/cycles/{cycleId}/close", content: null);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var contractor = await db.People.SingleAsync(p => p.ExternalRef == "CONTRACTOR1"); // not ADMIN's direct report

        var response = await client.PostAsJsonAsync(
            $"/api/cycles/{cycleId}/late-override", new LateOverrideRequest(contractor.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task PlatformAdmin_late_override_for_a_nonexistent_person_returns_404_not_500()
    {
        // L2 panel round-1 advisory: previously a nonexistent PersonId under the PlatformAdmin path
        // fell through to an unhandled DbUpdateException (FK violation) — a 500. Now checked
        // up front against the loaded org graph, same as the manager path.
        var (client, frameworkVersionId) = await AdminClientWithFrameworkAsync();
        var cycleId = await CreateDraftCycleAsync(client, frameworkVersionId);
        await client.PostAsync($"/api/cycles/{cycleId}/open", content: null);
        await client.PostAsync($"/api/cycles/{cycleId}/close", content: null);

        var response = await client.PostAsJsonAsync(
            $"/api/cycles/{cycleId}/late-override", new LateOverrideRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Manager_cannot_grant_a_late_override_for_a_departed_direct_report()
    {
        // L2 panel round-1 advisory: the manager-scope check previously ignored IsActive, so a
        // manager could grant an override for a report who has since left — ChainResolver itself
        // filters on IsActive, and this check now mirrors that convention.
        var (adminClient, frameworkVersionId) = await AdminClientWithFrameworkAsync();
        var cycleId = await CreateDraftCycleAsync(adminClient, frameworkVersionId);
        await adminClient.PostAsync($"/api/cycles/{cycleId}/open", content: null);
        await adminClient.PostAsync($"/api/cycles/{cycleId}/close", content: null);

        Guid emp1Id;
        using (var scope = _factory.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            var emp1 = await db.People.SingleAsync(p => p.ExternalRef == "EMP1");
            emp1Id = emp1.Id;
            emp1.Deactivate(); // still MGR1's ManagerPersonId edge, but no longer active
            await db.SaveChangesAsync();
        }

        var mgrClient = _factory.CreateClient();
        await HapApiFactory.SignInAsync(mgrClient, "MGR1");

        var response = await mgrClient.PostAsJsonAsync(
            $"/api/cycles/{cycleId}/late-override", new LateOverrideRequest(emp1Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var verifyScope = _factory.NewScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<HapDbContext>();
        var hasOverride = await verifyDb.CycleLateOverrides.AnyAsync(o => o.CycleId == cycleId && o.PersonId == emp1Id);
        Assert.False(hasOverride);
    }

    [Fact]
    public async Task Manager_can_grant_a_late_override_for_their_own_direct_report()
    {
        var (adminClient, frameworkVersionId) = await AdminClientWithFrameworkAsync();
        var cycleId = await CreateDraftCycleAsync(adminClient, frameworkVersionId);
        await adminClient.PostAsync($"/api/cycles/{cycleId}/open", content: null);
        await adminClient.PostAsync($"/api/cycles/{cycleId}/close", content: null);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var emp1 = await db.People.SingleAsync(p => p.ExternalRef == "EMP1");

        var mgrClient = _factory.CreateClient();
        await HapApiFactory.SignInAsync(mgrClient, "MGR1");

        var response = await mgrClient.PostAsJsonAsync(
            $"/api/cycles/{cycleId}/late-override", new LateOverrideRequest(emp1.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var grant = await response.Content.ReadFromJsonAsync<LateOverrideResponse>();
        Assert.Equal("Manager", grant!.GrantedByRole);
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Manager_cannot_grant_a_late_override_outside_their_own_direct_reports()
    {
        var (adminClient, frameworkVersionId) = await AdminClientWithFrameworkAsync();
        var cycleId = await CreateDraftCycleAsync(adminClient, frameworkVersionId);
        await adminClient.PostAsync($"/api/cycles/{cycleId}/open", content: null);
        await adminClient.PostAsync($"/api/cycles/{cycleId}/close", content: null);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var emp2 = await db.People.SingleAsync(p => p.ExternalRef == "EMP2"); // not MGR1's report

        var mgrClient = _factory.CreateClient();
        await HapApiFactory.SignInAsync(mgrClient, "MGR1");

        var response = await mgrClient.PostAsJsonAsync(
            $"/api/cycles/{cycleId}/late-override", new LateOverrideRequest(emp2.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // out-of-scope, not 403 (existence-leak convention)

        var hasOverride = await db.CycleLateOverrides.AnyAsync(o => o.CycleId == cycleId && o.PersonId == emp2.Id);
        Assert.False(hasOverride);
    }

    [Fact]
    public async Task Business_unit_onboarding_endpoint_sets_the_flag_and_404s_for_an_unknown_bu()
    {
        var (client, _) = await AdminClientWithFrameworkAsync();

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var bu02 = await db.BusinessUnits.SingleAsync(b => b.Code == "BU02");
        Assert.False(bu02.IsOnboarded); // sanity: fixture starts BU02 un-onboarded

        var response = await client.PostAsync($"/api/admin/business-units/{bu02.Id}/onboard", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var reloaded = await db.BusinessUnits.AsNoTracking().SingleAsync(b => b.Id == bu02.Id);
        Assert.True(reloaded.IsOnboarded);

        var notFound = await client.PostAsync($"/api/admin/business-units/{Guid.NewGuid()}/onboard", content: null);
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task CycleService_HasLateOverrideAsync_reflects_grants_directly()
    {
        // Exercises the actual primitive QUESTIONS.md Q-017a says HAP-8/HAP-9 must call — not just
        // the raw table a plain EF query could equally assert against.
        var (client, frameworkVersionId) = await AdminClientWithFrameworkAsync();
        var cycleId = await CreateDraftCycleAsync(client, frameworkVersionId);
        await client.PostAsync($"/api/cycles/{cycleId}/open", content: null);
        await client.PostAsync($"/api/cycles/{cycleId}/close", content: null);

        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var emp1 = await db.People.SingleAsync(p => p.ExternalRef == "EMP1");
        var svc = scope.ServiceProvider.GetRequiredService<Hap.Infrastructure.Cycles.CycleService>();

        Assert.False(await svc.HasLateOverrideAsync(cycleId, emp1.Id));

        await client.PostAsJsonAsync($"/api/cycles/{cycleId}/late-override", new LateOverrideRequest(emp1.Id));

        Assert.True(await svc.HasLateOverrideAsync(cycleId, emp1.Id));
    }

    [Fact]
    public async Task Create_cycle_against_an_unknown_framework_version_returns_404()
    {
        var (client, _) = await AdminClientWithFrameworkAsync();

        var response = await client.PostAsJsonAsync("/api/cycles",
            new CreateCycleRequest(Guid.NewGuid(), "2026-08", true));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
