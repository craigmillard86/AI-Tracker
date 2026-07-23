namespace Hap.Infrastructure.Email;

/// <summary>Port for sending one outbound email. The only production implementation
/// (<see cref="MailKitEmailSender"/>) can structurally reach mailpit only — see its class doc.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
