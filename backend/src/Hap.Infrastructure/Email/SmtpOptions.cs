namespace Hap.Infrastructure.Email;

/// <summary>The SMTP endpoint <see cref="MailKitEmailSender"/> sends through, bound from config
/// section "Smtp" (<c>IOptions&lt;SmtpOptions&gt;</c>). Defaults point at mailpit's local dev SMTP
/// listener (<c>localhost:1025</c>) for local/dev/test; the docker-compose <c>api</c> service
/// overrides both via <c>Smtp__Host</c>/<c>Smtp__Port</c> to the "mailpit" container DNS name.
///
/// There is deliberately no other configuration surface here (no auth, no TLS toggle, no remote-host
/// key of any kind anywhere in this codebase): mailpit is unauthenticated plaintext SMTP on the
/// compose network, and that is the ONLY destination this build can ever be pointed at.</summary>
public sealed class SmtpOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
}
