using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using Hap.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hap.Api.Tests;

/// <summary>
/// Self-provisions a disposable mailpit container (mirrors scripts/verify.sh's own disposable-Postgres
/// pattern: shell out to <c>docker run</c>, poll until ready, <c>docker rm -f</c> in teardown) so
/// <see cref="NotificationJobMailpitIntegrationTests"/> can prove behaviour against mailpit's REAL REST
/// API rather than the in-memory test doubles <c>HapApiFactory</c> swaps in everywhere else. Random,
/// collision-proof ports (an OS-assigned free TCP port each) and a GUID-suffixed container name so
/// parallel/repeated runs never collide, matching <c>verify.sh</c>'s own collision-proofing discipline.
/// </summary>
public sealed class MailpitFixture : IAsyncLifetime
{
    public int SmtpPort { get; private set; }
    public int ApiPort { get; private set; }
    public string ApiBaseUrl => $"http://localhost:{ApiPort}";

    private string _containerName = string.Empty;

    public async Task InitializeAsync()
    {
        _containerName = $"hap-test-mailpit-{Environment.ProcessId}-{Guid.NewGuid():N}";
        SmtpPort = GetFreeTcpPort();
        ApiPort = GetFreeTcpPort();

        var run = Process.Start(new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"run -d --rm --name {_containerName} " +
                        $"-p {SmtpPort}:1025 -p {ApiPort}:8025 axllent/mailpit:v1.20.0",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("Failed to start `docker run` for the test mailpit fixture.");
        await run.WaitForExitAsync();
        if (run.ExitCode != 0)
        {
            var stderr = await run.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"`docker run` for the test mailpit fixture failed: {stderr}");
        }

        using var http = new HttpClient { BaseAddress = new Uri(ApiBaseUrl) };
        var deadline = DateTime.UtcNow.AddSeconds(30);
        Exception? lastError = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await http.GetAsync("/api/v1/info");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
            await Task.Delay(500);
        }
        throw new InvalidOperationException(
            $"Test mailpit container {_containerName} did not become ready within 30s.", lastError);
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrEmpty(_containerName))
        {
            return;
        }
        using var rm = Process.Start(new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"rm -f {_containerName}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        });
        if (rm is not null)
        {
            await rm.WaitForExitAsync();
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

/// <summary>
/// The ONE real-mailpit test class for HAP-18 (story AC: "idempotence test via mailpit message count" /
/// "assert exact recipient sets + content via the mailpit API"). Runs the REAL
/// <see cref="MailKitEmailSender"/> + REAL <see cref="MailpitSentMailLedger"/> against the ephemeral
/// container <see cref="MailpitFixture"/> provisions — proving the whole send → search → dedup loop
/// against mailpit's actual REST API. Deliberately NOT in the "hap-db" collection: this fixture has its
/// own container lifecycle and no Postgres dependency, so it must not share (or be serialised by) that
/// collection.
///
/// <para><b>Schema caveat:</b> the recipient/subject content assertion below assumes mailpit v1.20.0's
/// documented <c>/api/v1/search</c> response shape (<c>messages[].Subject</c>,
/// <c>messages[].To[].Address</c>). <c>MailpitSentMailLedger.HasAtLeastOneMessage</c> itself does NOT
/// depend on this assumption — it only checks for a non-empty <c>messages</c> array / a positive count
/// field, deliberately loosely, and fails open on anything else (see its class doc).</para>
/// </summary>
public sealed class NotificationJobMailpitIntegrationTests : IClassFixture<MailpitFixture>
{
    private readonly MailpitFixture _mailpit;

    public NotificationJobMailpitIntegrationTests(MailpitFixture mailpit) => _mailpit = mailpit;

    private IEmailSender BuildSender() =>
        new MailKitEmailSender(Options.Create(new SmtpOptions { Host = "localhost", Port = _mailpit.SmtpPort }));

    private MailpitSentMailLedger BuildLedger()
    {
        var http = new HttpClient { BaseAddress = new Uri(_mailpit.ApiBaseUrl) };
        return new MailpitSentMailLedger(http, NullLogger<MailpitSentMailLedger>.Instance);
    }

    [Fact]
    public async Task Sending_then_searching_mailpit_finds_the_message_with_the_expected_recipient_and_subject()
    {
        var sender = BuildSender();
        var ledger = BuildLedger();
        var dedupToken = $"WUN-{Guid.NewGuid():N}-{DateTime.UtcNow:yyyyMMdd}";
        var to = "owner@synth.local";
        var subject = $"Overdue weekly update: Test Initiative (ref: {dedupToken})";

        Assert.False(await ledger.WasAlreadySentAsync(dedupToken));

        await sender.SendAsync(new EmailMessage(new[] { to }, subject, "Body text for the real-mailpit proof."));

        // mailpit's search indexing has been effectively immediate empirically in prior local testing,
        // but poll defensively rather than assuming instant consistency.
        var found = await PollUntilAsync(() => ledger.WasAlreadySentAsync(dedupToken), TimeSpan.FromSeconds(10));
        Assert.True(found, "Expected mailpit's search API to find the just-sent message by its dedup token.");

        using var http = new HttpClient { BaseAddress = new Uri(_mailpit.ApiBaseUrl) };
        var response = await http.GetAsync($"/api/v1/search?query={Uri.EscapeDataString(dedupToken)}");
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var message = doc.RootElement.GetProperty("messages")[0];

        var messageSubject = message.GetProperty("Subject").GetString();
        Assert.NotNull(messageSubject);
        Assert.Contains(dedupToken, messageSubject);

        var recipients = message.GetProperty("To").EnumerateArray()
            .Select(r => r.GetProperty("Address").GetString() ?? string.Empty)
            .ToList();
        Assert.Contains(to, recipients);
    }

    [Fact]
    public async Task Idempotence_running_the_send_check_twice_the_same_day_produces_exactly_one_mailpit_message()
    {
        var sender = BuildSender();
        var ledger = BuildLedger();
        var dedupToken = $"WUN-{Guid.NewGuid():N}-{DateTime.UtcNow:yyyyMMdd}";
        var to = "owner2@synth.local";
        var subject = $"Overdue weekly update: Idempotence Initiative (ref: {dedupToken})";

        // Mirrors NotificationJobService's own check-then-send-then-record loop directly against the
        // real ledger + sender, invoked "twice" as the AC requires ("running the jobs twice in one day
        // sends no duplicate emails").
        async Task RunOnceAsync()
        {
            if (await ledger.WasAlreadySentAsync(dedupToken))
            {
                return;
            }
            await sender.SendAsync(new EmailMessage(new[] { to }, subject, "Body."));
            await ledger.RecordSentAsync(dedupToken);
        }

        await RunOnceAsync();
        await PollUntilAsync(() => ledger.WasAlreadySentAsync(dedupToken), TimeSpan.FromSeconds(10));
        await RunOnceAsync(); // must be a no-op — the ledger already reports this token as sent

        using var http = new HttpClient { BaseAddress = new Uri(_mailpit.ApiBaseUrl) };
        var response = await http.GetAsync($"/api/v1/search?query={Uri.EscapeDataString(dedupToken)}");
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        // Exactly one message for this dedup token — the second RunOnceAsync must not have sent again.
        Assert.Equal(1, doc.RootElement.GetProperty("messages").GetArrayLength());
    }

    [Fact]
    public async Task Cycle_reminder_token_idempotence_produces_exactly_one_mailpit_message()
    {
        // FR-061 uses the same check-then-send-then-record loop and mailpit-message ledger as FR-037, but
        // with a CR- (cycle-reminder) dedup token. Prove that token shape dedups by mailpit message count
        // too — the same "running the jobs twice in one day sends no duplicates" guarantee against the real
        // mailpit search API (the CycleReminderJobTests cover the job's own loop with the in-memory ledger).
        var sender = BuildSender();
        var ledger = BuildLedger();
        var dedupToken = $"CR-{Guid.NewGuid():N}-{Guid.NewGuid():N}-T7-{DateTime.UtcNow:yyyyMMdd}";
        var to = "nonresponder@synth.local";
        var subject = $"Reminder: submit your \"2026-08\" self-assessment (ref: {dedupToken})";

        async Task RunOnceAsync()
        {
            if (await ledger.WasAlreadySentAsync(dedupToken))
            {
                return;
            }
            await sender.SendAsync(new EmailMessage(new[] { to }, subject, "Body."));
            await ledger.RecordSentAsync(dedupToken);
        }

        await RunOnceAsync();
        await PollUntilAsync(() => ledger.WasAlreadySentAsync(dedupToken), TimeSpan.FromSeconds(10));
        await RunOnceAsync(); // must be a no-op — the ledger already reports this token as sent

        using var http = new HttpClient { BaseAddress = new Uri(_mailpit.ApiBaseUrl) };
        var response = await http.GetAsync($"/api/v1/search?query={Uri.EscapeDataString(dedupToken)}");
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetProperty("messages").GetArrayLength());
    }

    private static async Task<bool> PollUntilAsync(Func<Task<bool>> check, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            if (await check())
            {
                return true;
            }
            await Task.Delay(250);
        }
        return await check();
    }
}
