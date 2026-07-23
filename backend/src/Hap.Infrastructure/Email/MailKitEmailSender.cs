using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Hap.Infrastructure.Email;

/// <summary>
/// The production <see cref="IEmailSender"/>: connects to <see cref="SmtpOptions.Host"/>/
/// <see cref="SmtpOptions.Port"/> with <see cref="SecureSocketOptions.None"/> (mailpit is
/// unauthenticated plaintext SMTP locally — no TLS, no auth, ever) and sends a plain-text
/// <see cref="MimeMessage"/>. One connect/send/disconnect per call — no pooling needed at this
/// synthetic-data scale (research D7).
///
/// <para><b>Structurally local-only.</b> This sender can only ever reach whatever
/// <see cref="SmtpOptions"/> resolves to, and <see cref="SmtpOptions"/> carries no remote-host
/// configuration key anywhere in this codebase (Program.cs binds it from config section "Smtp",
/// whose only two values in the whole repo are the class defaults and docker-compose's
/// <c>Smtp__Host=mailpit</c>/<c>Smtp__Port=1025</c>). Do not add one.</para>
/// </summary>
public sealed class MailKitEmailSender : IEmailSender
{
    private const string FromAddress = "hap-notifications@hig.local";

    private readonly SmtpOptions _options;

    public MailKitEmailSender(IOptions<SmtpOptions> options) => _options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(FromAddress));
        foreach (var address in message.To)
        {
            mime.To.Add(MailboxAddress.Parse(address));
        }
        mime.Subject = message.Subject;
        mime.Body = new TextPart("plain") { Text = message.Body };

        using var client = new SmtpClient();
        await client.ConnectAsync(_options.Host, _options.Port, SecureSocketOptions.None, cancellationToken)
            .ConfigureAwait(false);
        await client.SendAsync(mime, cancellationToken).ConfigureAwait(false);
        await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
    }
}
