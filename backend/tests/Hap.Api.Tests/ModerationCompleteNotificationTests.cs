using System.Net;
using System.Net.Http.Json;
using Hap.Domain.Assessments;
using Hap.Infrastructure;
using Hap.Infrastructure.Directory;
using Hap.Infrastructure.Frameworks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hap.Api.Tests;

/// <summary>
/// HAP-18 FR-057 moderation-complete notice (L3). When a manager moderates a report's self-assessment
/// (the <c>Submitted → Moderated</c> transition), the report receives a notice that a review happened —
/// fired only AFTER the moderation write commits, and carrying NO score, moderated value, or any other
/// person's data (the template names only the cycle + a deep link). The red-team / QA attack exactly this
/// body, so the assertion pins both the recipient and the no-data property.
///
/// <para>Fixture: BU01 onboarded; ADMIN (Platform Admin), MGR1 (manager of EMP1), EMP1 (Individual).</para>
/// </summary>
[Collection("hap-db")]
public sealed class ModerationCompleteNotificationTests
{
    private readonly HapApiFactory _factory;

    public ModerationCompleteNotificationTests(HapApiFactory factory) => _factory = factory;

    private RecordingEmailSender Recorder => (RecordingEmailSender)_factory.Emails.Inner;

    private async Task<HttpClient> SeedAsync()
    {
        await _factory.ResetAsync();
        _factory.Emails.Inner = new RecordingEmailSender();

        _factory.Directory.Inner = new StubDirectorySource(Snap.Of(
            new[] { Snap.Bu("BU01") },
            new[]
            {
                Snap.Person("ADMIN", "BU01"),
                Snap.Person("MGR1", "BU01"),
                Snap.Person("EMP1", "BU01", managerExternalRef: "MGR1"),
            }));
        using (var scope = _factory.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<DirectoryImportService>().SyncAsync();
            var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
            (await db.BusinessUnits.SingleAsync(b => b.Code == "BU01")).SetOnboarded(true);
            await db.SaveChangesAsync();
        }
        _factory.SeedUsers.Inner = new StubSeedUserSource(new[]
        {
            Snap.SeedUser("ADMIN", role: "Platform Admin"),
            Snap.SeedUser("MGR1", role: "Manager"),
            Snap.SeedUser("EMP1", role: "Individual"),
        });

        var admin = _factory.CreateClient();
        await HapApiFactory.SignInAsync(admin, "ADMIN");
        var framework = await (await admin.PostAsync("/api/admin/frameworks", null))
            .Content.ReadFromJsonAsync<FrameworkSeedResult>();
        var created = await admin.PostAsJsonAsync("/api/cycles", new CreateCycleRequest(framework!.VersionId, "2026-08", true));
        var cycle = await created.Content.ReadFromJsonAsync<CycleResponse>();
        await admin.PostAsync($"/api/cycles/{cycle!.Id}/open", null);
        return admin;
    }

    private async Task<HttpClient> ClientAsync(string externalRef)
    {
        var client = _factory.CreateClient();
        await HapApiFactory.SignInAsync(client, externalRef);
        return client;
    }

    private async Task SubmitSelfAsync(string externalRef, int score)
    {
        var client = await ClientAsync(externalRef);
        var form = (await (await client.GetAsync("/api/me/assessment")).Content.ReadFromJsonAsync<SelfAssessmentResponse>())!;
        await client.PutAsJsonAsync("/api/me/assessment/scores",
            new UpsertScoresRequest(form.Dimensions.Select(d => new ScoreEntry(d.DimensionId, score, null)).ToList()));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/me/assessment/submit", null)).StatusCode);
    }

    private async Task<(Guid AssessmentId, string Email)> Emp1AssessmentAsync()
    {
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var emp1 = await db.People.SingleAsync(p => p.ExternalRef == "EMP1");
        var assessmentId = await db.Set<Assessment>()
            .Where(a => a.PersonId == emp1.Id)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => a.Id)
            .FirstAsync();
        return (assessmentId, emp1.Email);
    }

    [Fact]
    [Trait("Category", "PrivacyReporting")]
    public async Task Moderating_a_report_emails_the_individual_a_notice_with_no_score_data()
    {
        await SeedAsync();
        await SubmitSelfAsync("EMP1", 2);
        var (assessmentId, emp1Email) = await Emp1AssessmentAsync();

        // Clean capture immediately before the moderation, so the ONLY email is the moderation-complete one.
        _factory.Emails.Inner = new RecordingEmailSender();

        var mgr1 = await ClientAsync("MGR1");
        var moderate = await mgr1.PutAsJsonAsync(
            $"/api/team/reviews/{assessmentId}", new ModerateReviewRequest(Array.Empty<ModerateDecision>()));
        Assert.Equal(HttpStatusCode.NoContent, moderate.StatusCode);

        var msg = Assert.Single(Recorder.Sent);
        Assert.Equal(new[] { emp1Email }, msg.To);
        Assert.Contains("reviewed", msg.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2026-08", msg.Body); // names the cycle …
        Assert.Contains("does not contain any of your", msg.Body); // … and self-declares it carries no scores
    }

    [Fact]
    public async Task A_mail_send_failure_does_not_fail_the_moderation_write()
    {
        await SeedAsync();
        await SubmitSelfAsync("EMP1", 2);
        var (assessmentId, _) = await Emp1AssessmentAsync();

        // The sender throws on every send: the notice is best-effort (swallowed + logged), so the
        // already-committed moderation must still return success.
        _factory.Emails.Inner = new ThrowingEmailSender();

        var mgr1 = await ClientAsync("MGR1");
        var moderate = await mgr1.PutAsJsonAsync(
            $"/api/team/reviews/{assessmentId}", new ModerateReviewRequest(Array.Empty<ModerateDecision>()));
        Assert.Equal(HttpStatusCode.NoContent, moderate.StatusCode);

        // …and the moderation actually persisted (state advanced past Submitted).
        using var scope = _factory.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<HapDbContext>();
        var state = await db.Set<Assessment>().Where(a => a.Id == assessmentId).Select(a => a.State).SingleAsync();
        Assert.Equal(AssessmentState.Moderated, state);
    }
}

/// <summary>An <see cref="Hap.Infrastructure.Email.IEmailSender"/> that always throws — proves the
/// FR-057 notice is best-effort and never fails an already-committed moderation.</summary>
public sealed class ThrowingEmailSender : Hap.Infrastructure.Email.IEmailSender
{
    public Task SendAsync(Hap.Infrastructure.Email.EmailMessage message, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("mail transport unavailable (injected test fault)");
}
